using System.Text.Json.Nodes;
using Lockflight.Cli.Canaries;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Tests.Ci;

namespace Lockflight.Cli.Tests.Canaries;

/// <summary>
/// Violations planted in front of the CI judge's trust checks, each in a scripted history of its own with a harmless twin that must
/// pass: a pull request that loosens the policy in its own <c>lockflight.yaml</c>, and a shallow clone that lacks the merge base.
/// </summary>
internal static class CiJudgeCanaries
{
    public const string PolicyName = "ci: a pull request that loosens lockflight.yaml is judged by the target branch's policy";
    public const string ShallowName = "ci: a shallow clone without the merge base is refused, not judged on a narrower range";

    public static async Task<IReadOnlyList<CanaryReport>> RunAsync(CancellationToken cancellationToken) =>
        [await PolicyFromBaseAsync(cancellationToken), await ShallowCloneAsync(cancellationToken)];

    private static async Task<CanaryReport> PolicyFromBaseAsync(CancellationToken cancellationToken)
    {
        using GitHistoryFixture repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        using TemporaryEvidence evidence = new();
        string main = await repository.Write("lockflight.yaml", "version: 1\nci:\n  maxRedCommits: 1\n").CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("001-x", cancellationToken);
        string oneRed = await repository.Write("a.cs", "a").CommitAsync("test: red 1", cancellationToken, null, "Lockflight-Red: 1");
        string twin = await RedWhyAsync(repository, oneRed, main, evidence.Folder, cancellationToken);
        if (twin.Contains("maxRedCommits", StringComparison.Ordinal))
        {
            return new CanaryReport(PolicyName, CanaryResult.Broken, $"one red commit under a cap of one was refused too ({twin}), so a refusal proves nothing");
        }

        string planted = await repository.Write("lockflight.yaml", "version: 1\nci:\n  maxRedCommits: 10\n").Write("b.cs", "b").CommitAsync("test: red 2", cancellationToken, null, "Lockflight-Red: 2");
        string why = await RedWhyAsync(repository, planted, main, evidence.Folder, cancellationToken);
        return why.Contains("more than ci.maxRedCommits (1)", StringComparison.Ordinal)
            ? new CanaryReport(PolicyName, CanaryResult.Caught, why)
            : new CanaryReport(PolicyName, CanaryResult.Missed, $"the pull request's own cap applied: {why}");
    }

    private static async Task<CanaryReport> ShallowCloneAsync(CancellationToken cancellationToken)
    {
        using GitHistoryFixture repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        using TemporaryEvidence evidence = new();
        string main = await repository.Write("README.md", "# x\n").CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("001-x", cancellationToken);
        string head = await repository.Write("a.cs", "a").CommitAsync("feat", cancellationToken);
        string[] args = ["ci", "--platform", "github", "--head", head, "--base", main, "--evidence", evidence.Folder, "--plan"];
        string source = $"file://{repository.Root.Replace('\\', '/')}";

        using GitHistoryFixture full = await GitHistoryFixture.CreateAsync(cancellationToken);
        await full.GitAsync(["fetch", "-q", source, "001-x", "main"], cancellationToken);
        (ExitCode twin, _, string twinError) = await TestConsole.RunAsync(args, string.Empty, full.Root);
        if (twin != ExitCode.Pass)
        {
            return new CanaryReport(ShallowName, CanaryResult.Broken, $"a full clone was refused too (exit {(int)twin}: {twinError}), so a refusal proves nothing");
        }

        using GitHistoryFixture shallow = await GitHistoryFixture.CreateAsync(cancellationToken);
        await shallow.GitAsync(["fetch", "-q", "--depth", "1", source, "001-x", "main"], cancellationToken);
        (ExitCode planted, _, string error) = await TestConsole.RunAsync(args, string.Empty, shallow.Root);
        return planted == ExitCode.Config && error.Contains("shallow", StringComparison.Ordinal)
            ? new CanaryReport(ShallowName, CanaryResult.Caught, error)
            : new CanaryReport(ShallowName, CanaryResult.Missed, $"exit {(int)planted}: {error}");
    }

    private static async Task<string> RedWhyAsync(GitHistoryFixture repository, string head, string main, string evidence, CancellationToken cancellationToken)
    {
        await TestConsole.RunAsync(["ci", "--platform", "github", "--head", head, "--base", main, "--evidence", evidence], string.Empty, repository.Root);
        JsonNode? document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(evidence, "evidence.json"), cancellationToken));
        return document?["checks"]?.AsArray().FirstOrDefault(check => check?["id"]?.GetValue<string>() == "red")?["why"]?.GetValue<string>() ?? "no red check in the evidence";
    }

    private sealed class TemporaryEvidence : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-canary-ci-");

        public string Folder => directory.FullName;

        public void Dispose() => TempDirectories.DeleteRecursively(directory);
    }
}
