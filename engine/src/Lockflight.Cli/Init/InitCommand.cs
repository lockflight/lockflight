using Lockflight.Cli.Adoption;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Ledger;
using Lockflight.Cli.Linking;
using Lockflight.Cli.Projects;

namespace Lockflight.Cli.Init;

/// <summary>
/// <c>lockflight init</c> — lite or standard profile onboarding: manifest, tool pin, git-ignored local state, Claude Code plugin settings and one
/// CI job; then links the engine. Idempotent: existing content is kept and merged into, never overwritten. As a sanctioned
/// writer of protected files it records every change in the ledger.
/// </summary>
internal sealed class InitCommand(CliContext context)
{
    /// <summary><c>[--profile lite|standard] [--ci pr|manual|none] [--no-link]</c> in any order.</summary>
    public async Task<ExitCode> RunAsync(string[] options, CancellationToken cancellationToken)
    {
        bool link = true;
        InitProfile profile = InitProfile.Lite;
        CiTrigger ci = CiTrigger.PullRequest;
        for (int index = 0; index < options.Length; index++)
        {
            switch (options[index])
            {
                case "--no-link":
                    link = false;
                    break;
                case "--profile" when index + 1 < options.Length:
                    string name = options[++index];
                    if (!InitProfileNames.TryParse(name, out profile))
                    {
                        return await RefuseAsync($"unknown profile '{name}'; use lite or standard");
                    }

                    break;
                case "--profile":
                    return await RefuseAsync("--profile needs a value: lite or standard");
                case "--ci" when index + 1 < options.Length:
                    string trigger = options[++index];
                    if (!CiTriggerNames.TryParse(trigger, out ci))
                    {
                        return await RefuseAsync($"unknown CI trigger '{trigger}'; use pr, manual or none");
                    }

                    break;
                case "--ci":
                    return await RefuseAsync("--ci needs a value: pr, manual or none");
                default:
                    return await RefuseAsync($"unknown init option '{options[index]}'; use --profile lite|standard, --ci pr|manual|none or --no-link");
            }
        }

        return await InitializeAsync(link, profile, ci, cancellationToken);
    }

    private async Task<ExitCode> InitializeAsync(bool link, InitProfile profile, CiTrigger ci, CancellationToken cancellationToken)
    {
        string root = ProjectRoot();
        if (await PreflightProblemAsync(root, cancellationToken) is string problem)
        {
            return await RefuseAsync($"{problem}; nothing was written");
        }

        string[] testFrameworks = [.. ProjectFiles.TestProjects(root).SelectMany(project => ProjectFiles.TargetFrameworks(root, project)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
        IReadOnlyList<string> dotnetChannels = DotnetChannels.For(testFrameworks);
        // A workspace shell: the child directories that look like units go into a new manifest, and the child
        // repositories into the shell's .gitignore so the shell's git never tracks their files.
        IReadOnlyList<UnitCandidate> units = UnitDiscovery.Find(root);
        string manifest = units.Count == 0 ? Templates.Manifest(profile) : ManifestListEditor.Append(Templates.Manifest(profile), "units", [.. units.Select(unit => unit.ManifestEntry)]) ?? Templates.Manifest(profile);
        string[] childRepositories = [.. units.Where(unit => unit.OwnRepository).Select(unit => unit.Path + "/")];
        List<InitStep> steps =
        [
            await WriteIfMissingAsync(root, Manifest.FileName, manifest, cancellationToken),
            await TransformAsync(root, ToolManifestWriter.RelativePathIn(root), ToolManifestWriter.Pin, cancellationToken),
            await TransformAsync(root, ".gitignore", existing => GitIgnoreUpdater.Update(existing, childRepositories), cancellationToken),
            await TransformAsync(root, ".claude/settings.json", ClaudeSettingsMerger.Merge, cancellationToken),
        ];
        if (ci != CiTrigger.None)
        {
            steps.Add(await WriteIfMissingAsync(root, Templates.WorkflowPath, Templates.Workflow(dotnetChannels, ci), cancellationToken));
        }

        if (profile == InitProfile.Standard)
        {
            steps.Add(await TransformAsync(root, ".gitattributes", GitAttributesUpdater.Update, cancellationToken));
        }

        foreach (InitStep step in steps)
        {
            await context.Output.WriteLineAsync($"{Describe(step.Outcome),-10} {step.RelativePath}");
        }

        if (steps.SingleOrDefault(step => step.RelativePath == Templates.WorkflowPath) is InitStep workflow)
        {
            await ExplainDotnetChannelsAsync(workflow.Outcome, testFrameworks, dotnetChannels);
        }

        await RecordAsync(root, steps, cancellationToken);

        if (!link)
        {
            return ExitCode.Pass;
        }

        CliContext rootContext = context with { WorkingDirectory = root };
        ExitCode linkResult = await new LinkCommand(rootContext).RunAsync(cancellationToken);
        if (linkResult == ExitCode.Pass)
        {
            await context.Output.WriteLineAsync($"Next: commit these files, then run `{EngineCommand.Linked} doctor` from the project root.");
        }

        return linkResult;
    }

    /// <summary>Says why CI installs more than the engine's SDK, or that an existing workflow may need it.</summary>
    private async Task ExplainDotnetChannelsAsync(FileOutcome workflow, string[] testFrameworks, IReadOnlyList<string> dotnetChannels)
    {
        if (dotnetChannels.Count < 2)
        {
            return;
        }

        string channels = string.Join(", ", dotnetChannels);
        string frameworks = string.Join(", ", testFrameworks);
        await context.Output.WriteLineAsync(workflow == FileOutcome.Created
            ? $"CI installs .NET {channels}: the test projects target {frameworks}, and tests need each runtime. Locally, gates.test.framework in {Manifest.FileName} picks one target when a runtime is missing."
            : $"Note: the test projects target {frameworks}; {Templates.WorkflowPath} already existed, so check that its setup-dotnet installs {channels}.");
    }

    /// <summary>What init cannot merge into, checked before it writes anything so a refusal leaves the project as it was.</summary>
    private static async Task<string?> PreflightProblemAsync(string root, CancellationToken cancellationToken)
    {
        string toolManifest = ToolManifestWriter.RelativePathIn(root);
        string toolManifestPath = Path.Combine(root, toolManifest);
        string? tools = File.Exists(toolManifestPath) ? await File.ReadAllTextAsync(toolManifestPath, cancellationToken) : null;
        return ToolManifestWriter.Problem(tools) is string problem ? $"{toolManifest} {problem}" : null;
    }

    /// <summary>The nearest existing Lockflight project, else the current directory.</summary>
    private string ProjectRoot() =>
        ManifestLocator.Discover(context.WorkingDirectory) is { Manifest: Manifest manifest } ? manifest.Root : context.WorkingDirectory;

    private static async Task<InitStep> WriteIfMissingAsync(string root, string relativePath, string content, CancellationToken cancellationToken)
    {
        string path = Path.Combine(root, relativePath);
        if (File.Exists(path))
        {
            return new InitStep(relativePath, FileOutcome.Unchanged);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return new InitStep(relativePath, FileOutcome.Created);
    }

    private static async Task<InitStep> TransformAsync(
        string root, string relativePath, Func<string?, (string Content, bool Changed)> transform, CancellationToken cancellationToken)
    {
        string path = Path.Combine(root, relativePath);
        string? existing = File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
        (string content, bool changed) = transform(existing);
        if (!changed)
        {
            return new InitStep(relativePath, FileOutcome.Unchanged);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return new InitStep(relativePath, existing is null ? FileOutcome.Created : FileOutcome.Updated);
    }

    private async Task RecordAsync(string root, List<InitStep> steps, CancellationToken cancellationToken)
    {
        LedgerWriter ledger = new(root, context.Time);
        foreach (InitStep step in steps.Where(step => step.Outcome != FileOutcome.Unchanged))
        {
            await ledger.AppendAsync(new LedgerEvent(LedgerEventType.ConfigWritten, $"init:{step.RelativePath}", Describe(step.Outcome), null), cancellationToken);
        }
    }

    private async Task<ExitCode> RefuseAsync(string message)
    {
        await context.Error.WriteLineAsync($"lockflight: {message}");
        return ExitCode.Config;
    }

    private static string Describe(FileOutcome outcome) => outcome switch
    {
        FileOutcome.Created => "created",
        FileOutcome.Updated => "updated",
        _ => "unchanged",
    };
}
