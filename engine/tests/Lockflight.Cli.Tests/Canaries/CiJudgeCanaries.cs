using System.Text.Json.Nodes;
using Lockflight.Cli.Canaries;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Tests.Ci;

namespace Lockflight.Cli.Tests.Canaries;

/// <summary>
/// Violations planted in front of the CI judge's trust checks, each in a scripted history of its own with a harmless twin that must
/// pass: a pull request that loosens the policy in its own <c>lockflight.yaml</c>, a shallow clone that lacks the merge base, and,
/// on a real build, a red trailer on a commit that already holds the implementation.
/// </summary>
internal static class CiJudgeCanaries
{
    public const string PolicyName = "ci: a pull request that loosens lockflight.yaml is judged by the target branch's policy";
    public const string ShallowName = "ci: a shallow clone without the merge base is refused, not judged on a narrower range";
    public const string MissingArtifactName = "ci report: a missing judge artifact is NOT-EARNED, never a pass";
    public const string StaleReviewName = "ci report: a review of another head is ignored, not joined";
    public const string ImplementationName = "ci: a red trailer on a commit that holds the implementation is rejected (GREEN_BEFORE)";

    private const string ShippingTest = "    [Fact]\n    public void ShippingIsTwiceTheWeight() => Assert.Equal(4m, new Price().Shipping(2m));";

    public static async Task<IReadOnlyList<CanaryReport>> RunAsync(CancellationToken cancellationToken) =>
        [await PolicyFromBaseAsync(cancellationToken), await ShallowCloneAsync(cancellationToken), await ImplementationInRedAsync(cancellationToken), MissingArtifact(), StaleReview()];

    private static CanaryReport MissingArtifact()
    {
        string head = new('a', 40);
        string twin = CiReportCommand.Join(ReportEvidence(head), head, null)["contexts"]!["gates"]!.GetValue<string>();
        if (twin != "PASS")
        {
            return new CanaryReport(MissingArtifactName, CanaryResult.Broken, $"a complete passing artifact reported {twin} too, so a refusal proves nothing");
        }

        string planted = CiReportCommand.Join(null, head, null)["contexts"]!["gates"]!.GetValue<string>();
        return planted == "NOT-EARNED"
            ? new CanaryReport(MissingArtifactName, CanaryResult.Caught, "NOT-EARNED: the judge did not finish")
            : new CanaryReport(MissingArtifactName, CanaryResult.Missed, planted);
    }

    private static CanaryReport StaleReview()
    {
        string head = new('a', 40);
        JsonObject Review(string reviewed) => new() { ["schema"] = "lockflight.ci-review/v1", ["head"] = reviewed, ["verdict"] = "PASS", ["why"] = "no findings" };
        string twin = CiReportCommand.Join(ReportEvidence(head), head, Review(head))["contexts"]!["lockflight/judge"]!.GetValue<string>();
        if (twin != "PASS")
        {
            return new CanaryReport(StaleReviewName, CanaryResult.Broken, $"a review of the right head was not joined either ({twin})");
        }

        string planted = CiReportCommand.Join(ReportEvidence(head), head, Review(new string('c', 40)))["contexts"]!["lockflight/judge"]!.GetValue<string>();
        return planted == "NOT-EARNED"
            ? new CanaryReport(StaleReviewName, CanaryResult.Caught, "NOT-EARNED: the review is SKIPPED (stale)")
            : new CanaryReport(StaleReviewName, CanaryResult.Missed, planted);
    }

    /// <summary>A judge's evidence whose every check passed, with a review the class requires.</summary>
    private static JsonObject ReportEvidence(string head) => new()
    {
        ["schema"] = CiEvidence.Schema,
        ["head"] = head,
        ["event"] = "pr",
        ["checks"] = new JsonArray([.. Enum.GetValues<CheckId>().Select(id => (JsonNode)new JsonObject
        {
            ["id"] = CheckIdNames.ToName(id),
            ["context"] = StatusContextNames.ToName(StatusContextNames.Of(id)),
            ["verdict"] = id == CheckId.Review ? "SKIPPED" : "PASS",
            ["requirement"] = "required",
            ["why"] = id == CheckId.Review ? "joined by the report" : "ok",
        })]),
    };

    private static async Task<CanaryReport> ImplementationInRedAsync(CancellationToken cancellationToken)
    {
        using GitHistoryFixture repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        using TemporaryEvidence evidence = new();
        string main = await repository.WriteSolution(string.Empty, string.Empty).CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync("001-x", cancellationToken);
        string stub = await repository.WriteCode("    public decimal Shipping(decimal kilograms) => throw new NotImplementedException();", ShippingTest)
            .CommitAsync("test: red", cancellationToken, null, "Lockflight-Red: 1");
        string twin = await CheckAsync(repository, stub, main, evidence.Folder, "red", cancellationToken);
        if (!twin.StartsWith("PASS", StringComparison.Ordinal))
        {
            return new CanaryReport(ImplementationName, CanaryResult.Broken, $"a red commit with a stub was refused too ({twin}), so a refusal proves nothing");
        }

        await repository.ResetAsync(main, cancellationToken);
        string planted = await repository.WriteCode("    public decimal Shipping(decimal kilograms) => kilograms * 2m;", ShippingTest)
            .CommitAsync("test: red", cancellationToken, null, "Lockflight-Red: 1");
        string red = await CheckAsync(repository, planted, main, evidence.Folder, "red", cancellationToken);
        return red.StartsWith("FAIL", StringComparison.Ordinal) && red.Contains("GREEN_BEFORE", StringComparison.Ordinal)
            ? new CanaryReport(ImplementationName, CanaryResult.Caught, red)
            : new CanaryReport(ImplementationName, CanaryResult.Missed, red);
    }

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
        string check = await CheckAsync(repository, head, main, evidence, "red", cancellationToken);
        return check[(check.IndexOf(' ', StringComparison.Ordinal) + 1)..];
    }

    /// <summary>The verdict and why of one check after a judge run, as "VERDICT why".</summary>
    private static async Task<string> CheckAsync(GitHistoryFixture repository, string head, string main, string evidence, string id, CancellationToken cancellationToken)
    {
        await TestConsole.RunAsync(
            ["ci", "--platform", "github", "--head", head, "--base", main, "--evidence", evidence], string.Empty, repository.Root, new Dictionary<string, string> { [EnvironmentVariables.Temp] = evidence });
        JsonNode? document = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(evidence, "evidence.json"), cancellationToken));
        JsonNode? found = document?["checks"]?.AsArray().FirstOrDefault(node => node?["id"]?.GetValue<string>() == id);
        return found is null ? $"ERROR no {id} check in the evidence" : $"{found["verdict"]?.GetValue<string>()} {found["why"]?.GetValue<string>()}";
    }

    private sealed class TemporaryEvidence : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-canary-ci-");

        public string Folder => directory.FullName;

        public void Dispose() => TempDirectories.DeleteRecursively(directory);
    }
}
