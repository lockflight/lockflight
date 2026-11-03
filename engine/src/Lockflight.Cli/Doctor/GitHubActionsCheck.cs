using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Lockflight.Cli.Init;
using Lockflight.Cli.Processes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Doctor;

/// <summary>
/// Whether GitHub has ever run the generated workflow on <c>push</c> or <c>pull_request</c>. On some new repositories GitHub runs
/// no workflow until someone enables them in the Actions tab ("Workflows aren't being run on this repository"), even though the
/// Actions permissions API says they are enabled, and a required <c>gates</c> check then waits forever as "Expected — Waiting
/// for status to be reported". Doctor's only network call: it asks the <c>gh</c> CLI under a short time limit, and is skipped
/// (null) whenever it cannot answer — no workflow with those triggers, an origin that is not on github.com or is a fork (whose
/// pull requests run upstream), <c>gh</c> missing, signed out, offline or slow, a workflow GitHub does not know yet — so it
/// never fails doctor.
/// </summary>
internal static class GitHubActionsCheck
{
    private const string Name = "github actions";
    private const string Host = "github.com";
    private const string ScpPrefix = "git@" + Host + ":";
    private const string GitSuffix = ".git";
    private const string WorkflowFile = "lockflight-gates.yml";
    private const string Push = "push";
    private const string PullRequest = "pull_request";

    private static readonly TimeSpan GitTimeLimit = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GhTimeLimit = TimeSpan.FromSeconds(10);
    private static readonly string[] JudgedEvents = [Push, PullRequest];

    /// <param name="processes">Starts <c>git</c> and <c>gh</c>; tests replace it so that nothing reaches the network.</param>
    public static async Task<DoctorCheck?> CheckAsync(string root, RunTimedProcess processes, CancellationToken cancellationToken)
    {
        if (!RunsOnPushOrPullRequest(root))
        {
            return null;
        }

        ProcessResult? remote = await TryRunAsync(processes, "git", ["remote", "get-url", "origin"], root, GitTimeLimit, cancellationToken);
        if (!Succeeded(remote) || RepositoryOf(remote.Output.Trim()) is not string repository)
        {
            return null;
        }

        // A fork's pull requests run in the upstream repository, and GitHub disables workflows on forks on purpose.
        ProcessResult? details = await GhApiAsync(processes, $"repos/{repository}", root, cancellationToken);
        if (!Succeeded(details) || IsFork(details.Output) is not false)
        {
            return null;
        }

        bool anyRun = false;
        foreach (string trigger in JudgedEvents)
        {
            ProcessResult? runs = await GhApiAsync(processes, $"repos/{repository}/actions/workflows/{WorkflowFile}/runs?event={trigger}&per_page=1", root, cancellationToken);
            if (!Succeeded(runs) || RunCount(runs.Output) is not int count)
            {
                return null;
            }

            if (count > 0)
            {
                anyRun = true;
                break;
            }
        }

        return anyRun
            ? new(Name, CheckStatus.Pass, $"GitHub has run {Templates.WorkflowPath} on push or pull_request in {repository}", null)
            : new(Name, CheckStatus.Warn,
                $"GitHub has no push or pull_request run of {Templates.WorkflowPath} in {repository}: workflows may not be enabled on the repository, and a required gates check then waits as \"Expected — Waiting for status to be reported\"",
                $"open https://{Host}/{repository}/actions and, if it says \"Workflows aren't being run on this repository\", enable them; then push to main or open a pull request");
    }

    /// <summary>
    /// <c>owner/name</c> of a remote on github.com (https, <c>ssh://</c> or <c>git@github.com:</c>, with or without <c>.git</c>);
    /// null for any other host, a path, or a name GitHub would not accept.
    /// </summary>
    public static string? RepositoryOf(string remote)
    {
        string? path = null;
        if (remote.StartsWith(ScpPrefix, StringComparison.OrdinalIgnoreCase))
        {
            path = remote[ScpPrefix.Length..];
        }
        else if (Uri.TryCreate(remote, UriKind.Absolute, out Uri? uri)
            && uri.Scheme is "https" or "http" or "ssh" or "git"
            && uri.Host.Equals(Host, StringComparison.OrdinalIgnoreCase)
            && uri.Query.Length == 0 && uri.Fragment.Length == 0)
        {
            path = uri.AbsolutePath;
        }

        string[] parts = (path ?? string.Empty).Trim('/').Split('/');
        if (parts.Length != 2)
        {
            return null;
        }

        string owner = parts[0];
        string name = parts[1].EndsWith(GitSuffix, StringComparison.OrdinalIgnoreCase) ? parts[1][..^GitSuffix.Length] : parts[1];
        bool valid = owner.Length > 0 && owner.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
            && name.Length > 0 && name is not "." and not ".."
            && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
        return valid ? $"{owner}/{name}" : null;
    }

    private static bool RunsOnPushOrPullRequest(string root)
    {
        string path = Path.Combine(root, Templates.WorkflowPath.Replace('/', Path.DirectorySeparatorChar));
        YamlStream stream = new();
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            stream.Load(new StringReader(File.ReadAllText(path)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or YamlException or ArgumentException or InvalidOperationException)
        {
            return false;
        }

        // `on:` is a single event, a list of events, or a mapping keyed by event.
        YamlNode? triggers = stream.Documents.Count > 0 && stream.Documents[0].RootNode is YamlMappingNode workflow
            && workflow.Children.TryGetValue(new YamlScalarNode("on"), out YamlNode? on) ? on : null;
        IEnumerable<YamlNode> events = triggers switch
        {
            YamlScalarNode single => [single],
            YamlSequenceNode list => list.Children,
            YamlMappingNode keyed => keyed.Children.Keys,
            _ => [],
        };
        return events.OfType<YamlScalarNode>().Any(trigger => trigger.Value is string name && JudgedEvents.Contains(name));
    }

    private static Task<ProcessResult?> GhApiAsync(RunTimedProcess processes, string endpoint, string root, CancellationToken cancellationToken) =>
        TryRunAsync(processes, "gh", ["api", "--hostname", Host, endpoint], root, GhTimeLimit, cancellationToken);

    private static bool Succeeded([NotNullWhen(true)] ProcessResult? result) => result is { ExitCode: 0, TimedOut: false };

    private static bool? IsFork(string json) =>
        Parse(json) is JsonElement repository && repository.TryGetProperty("fork", out JsonElement fork) && fork.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? fork.GetBoolean()
            : null;

    private static int? RunCount(string json) =>
        Parse(json) is JsonElement runs && runs.TryGetProperty("total_count", out JsonElement count) && count.TryGetInt32(out int total)
            ? total
            : null;

    // The root object of GitHub's JSON answer; null when the output is something else.
    private static JsonElement? Parse(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // A tool that is not installed is a skip, not an error.
    private static async Task<ProcessResult?> TryRunAsync(
        RunTimedProcess processes, string fileName, IReadOnlyList<string> arguments, string root, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        try
        {
            return await processes(fileName, arguments, root, timeLimit, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ExecutableNotFoundException)
        {
            return null;
        }
    }
}
