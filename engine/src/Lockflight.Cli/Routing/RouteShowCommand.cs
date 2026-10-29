using System.Text;
using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Routing;

/// <summary>
/// <c>lockflight route show [--base &lt;ref&gt;] [--head &lt;ref&gt;] [--json]</c>: the change class of the current branch and working
/// tree against the target branch (default: the branch <c>origin/HEAD</c> names, else <c>main</c>), its signals and what it
/// requires, in the pull request comment's form. Read only and open to the agent, which calls it at intake; it is advice: the
/// policy is the working tree's manifest, and CI recomputes the class from the base branch's. With <c>--head</c> it reads a
/// commit pair as CI does instead: the diff of <c>merge-base(base, head)..head</c> under the base commit's manifest. Exit 1 when a
/// routing rule fails (<c>routing.criticalMove</c>), 3 when git fails, 4 when the base is unknown or a manifest is invalid.
/// </summary>
internal sealed class RouteShowCommand(CliContext context) : ICommand
{
    public const string BaseOption = "--base";
    public const string HeadOption = "--head";
    public const string JsonFlag = "--json";

    private const string DefaultBranch = "main";

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync(arguments, context.Error, cancellationToken) is not CommandOptions options)
        {
            return ExitCode.Config;
        }

        ManifestLoadResult loaded = ManifestLocator.Discover(context.WorkingDirectory);
        if (loaded.Status == ManifestLoadStatus.Invalid)
        {
            await context.Error.WriteLineAsync($"lockflight route show: {loaded.Error?.Describe()}; a manifest that does not validate gives no class");
            return ExitCode.Config;
        }

        Manifest? manifest = loaded.Manifest;
        string root = manifest?.Root ?? context.WorkingDirectory;
        string baseRef = options.Value(BaseOption) ?? await Git.DefaultBranchAsync(root, DefaultBranch, cancellationToken);
        string? headRef = options.Value(HeadOption);
        ChangeSetReading reading = await ChangeSetReader.ReadAsync(root, baseRef, headRef, cancellationToken);
        if (reading.ChangeSet is not ChangeSet changeSet)
        {
            await context.Error.WriteLineAsync($"lockflight route show: {reading.Message}");
            return reading.Status == ChangeSetReadStatus.NoBase ? ExitCode.Config : ExitCode.Error;
        }

        RoutingPolicy policy;
        if (headRef is null)
        {
            string preset = RoutingPresetNames.ToName((manifest?.Routing ?? RoutingSettings.Default).Preset);
            policy = RoutingPolicy.From(manifest, new PolicySource(baseRef, "working tree", ManifestBlob: null, preset));
        }
        else
        {
            RoutingPolicyReading policyReading = await RoutingPolicyReader.ReadAsync(root, baseRef, cancellationToken);
            if (policyReading.Policy is not RoutingPolicy basePolicy)
            {
                await context.Error.WriteLineAsync($"lockflight route show: {policyReading.Message}");
                return policyReading.Status == RoutingPolicyReadStatus.GitFailed ? ExitCode.Error : ExitCode.Config;
            }

            policy = basePolicy;
            manifest = null;
        }
        Classification classification = ChangeClassifier.Classify(changeSet, policy, []);
        RequirementSet requirements = ClassRequirements.For(classification.Class, policy);
        if (options.Flag(JsonFlag))
        {
            await WriteJsonAsync(classification, requirements, cancellationToken);
        }
        else
        {
            await WriteTextAsync(classification, requirements, policy, manifest, baseRef, headRef);

        }

        return classification.Failures.Count == 0 ? ExitCode.Pass : ExitCode.Fail;
    }

    private async Task WriteTextAsync(Classification classification, RequirementSet requirements, RoutingPolicy policy, Manifest? manifest, string baseRef, string? headRef)
    {
        if (headRef is not null)
        {
            await context.Output.WriteLineAsync(
                $"route show (engine {ToolPin.RunningVersion}): {headRef} since its merge base with {baseRef}, judged by {baseRef}'s {Manifest.FileName} (preset {policy.Source.Preset}), as CI judges it");
        }
        else
        {
            string policyNote = manifest is null
                ? $"no {Manifest.FileName}: the default preset"
                : manifest.Routing.FromManifest ? $"the working tree's {Manifest.FileName}" : $"the default preset ({Manifest.FileName} has no routing section)";
            await context.Output.WriteLineAsync($"route show (engine {ToolPin.RunningVersion}): changes since the merge base with {baseRef}, judged by {policyNote}");
            await context.Output.WriteLineAsync("local policy, not the base branch: CI recomputes the class from the base branch's manifest");
        }

        await context.Output.WriteLineAsync();
        await context.Output.WriteLineAsync(RoutingComment.ClassLine(classification, policy));
        await context.Output.WriteLineAsync(RoutingComment.NeedsLine(requirements));
        if (classification.Signals.Count > 0)
        {
            await context.Output.WriteLineAsync();
            await context.Output.WriteLineAsync("signals:");
            foreach (ChangeSignal signal in classification.Signals)
            {
                string where = signal.Where.Length == 0 ? string.Empty : $" {signal.Where}";
                await context.Output.WriteLineAsync(
                    $"  {signal.Id} → {ChangeClassNames.ToName(signal.RaisesTo)} ({SignalStrengthNames.ToName(signal.Strength)}){where}: {signal.Detail}");
            }
        }

        foreach (string failure in classification.Failures)
        {
            await context.Output.WriteLineAsync($"FAIL {failure}");
        }
    }

    /// <summary>The evidence's <c>changeClass</c> block and the requirement set, as <c>{ "changeClass": …, "requirements": […] }</c>.</summary>
    private async Task WriteJsonAsync(Classification classification, RequirementSet requirements, CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new();
        await using (Utf8JsonWriter writer = new(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("changeClass");
            ClassificationJson.Write(writer, classification, ToolPin.RunningVersion);
            writer.WriteStartArray("requirements");
            foreach (Requirement requirement in requirements.Items)
            {
                writer.WriteStartObject();
                writer.WriteString("kind", RequirementNames.ToName(requirement.Kind));
                writer.WriteString("level", RequirementNames.ToName(requirement.Level));
                if (requirement.Threshold is int threshold)
                {
                    writer.WriteNumber("threshold", threshold);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        await context.Output.WriteLineAsync(Encoding.UTF8.GetString(buffer.ToArray()).AsMemory(), cancellationToken);
    }
}
