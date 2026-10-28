using System.Text.RegularExpressions;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Review;

/// <summary>
/// Confirms blocker/major findings by running the reviewer's reproduction <c>[Fact]</c>s: a reproduction that compiles and
/// fails confirms the finding, one that passes rejects it, anything else leaves it unverified. Each reproduction is built in
/// the test project nearest the cited file (<see cref="NearestTestProject"/>), in isolation from the developer's files and
/// build output (<see cref="ReproductionBuild"/>). All reproductions of a project are built together; compiler errors name the
/// reproduction they are in, so a broken one is set aside and the rest are built again, and a using directive the project's
/// files have but the generated file cannot resolve is dropped. A new finding whose reproduction does not compile gets one
/// repair: the reviewer sees the compiler errors and the signatures of the cited code and answers with a fixed reproduction.
/// A repaired reproduction that fails confirms the finding and replaces the first one; one that passes leaves the finding
/// unverified (<see cref="ReproductionResult.RepairedPassed"/>), because only the reviewer's first, unprompted test may reject
/// a finding. Building and running reproductions executes the project's own build logic and test fixtures, like any gate run.
/// </summary>
internal static partial class Reproductions
{
    private const int MaxUsingRepairs = 3;
    private const int MaxSignatureCharacters = 6_000;

    /// <summary>
    /// The finding before its reproduction runs: blocker/major findings are unverified (<see cref="ReproductionResult.NotRun"/> when they
    /// have a runnable reproduction, else <see cref="ReproductionResult.None"/>), minor and nit findings are reported.
    /// </summary>
    public static ReviewFinding Unjudged(ReviewFinding finding) => finding.NeedsReproduction
        ? finding with { Outcome = FindingOutcome.Unverified, Reproduced = IsRunnable(finding) ? ReproductionResult.NotRun : ReproductionResult.None }
        : finding with { Outcome = FindingOutcome.Reported, Reproduced = ReproductionResult.None };

    /// <summary>Whether the finding needs a reproduction and has one with a recognisable test method.</summary>
    public static bool IsRunnable(ReviewFinding finding) =>
        finding.NeedsReproduction && finding.Reproduction is string reproduction && MethodName(reproduction) is not null;

    /// <param name="repairable">The ids of findings whose reproduction may be repaired: new findings, not ones open from earlier runs.</param>
    public static async Task<IReadOnlyList<ReviewFinding>> ConfirmAsync(
        IReadOnlyList<ReviewFinding> findings,
        IReadOnlyList<TestApp> apps,
        string root,
        TimeSpan timeLimit,
        ReproductionRepairer? repairer,
        IReadOnlySet<string> repairable,
        CancellationToken cancellationToken)
    {
        ReviewFinding[] judged = [.. findings.Select(Unjudged)];
        ReproductionCandidate[] candidates = [.. judged
            .Select((finding, index) => (finding, index))
            .Where(item => IsRunnable(item.finding))
            .Select(item => new ReproductionCandidate(item.index, MethodName(item.finding.Reproduction!)!, item.finding.Reproduction!))];
        if (candidates.Length == 0 || apps.Count == 0)
        {
            return judged;
        }

        Dictionary<int, TestApp> chosen = candidates.ToDictionary(candidate => candidate.Index, candidate => NearestTestProject.Choose(apps, root, judged[candidate.Index].File));
        using ReviewBuildFolder folder = ReviewBuildFolder.Acquire(root);
        foreach ((int index, ReproductionVerdict verdict) in await JudgeAsync(candidates, chosen, root, folder.Name, timeLimit, cancellationToken))
        {
            judged[index] = judged[index] with
            {
                Outcome = verdict.Result switch
                {
                    ReproductionResult.Failed => FindingOutcome.Confirmed,
                    ReproductionResult.Passed => FindingOutcome.Rejected,
                    _ => FindingOutcome.Unverified,
                },
                Reproduced = verdict.Result,
                ReproductionAttempts = 1,
                CompileErrors = verdict.Errors is null ? [] : [verdict.Errors],
                ReproductionProject = ProjectPath(chosen[index], root),
            };
        }

        return repairer is null ? judged : await RepairAsync(judged, candidates, chosen, root, folder.Name, timeLimit, repairer, repairable, cancellationToken);
    }

    /// <summary>
    /// The reproduction's test method: the first method with an xUnit, NUnit or MSTest test attribute (<c>[Fact]</c>, <c>[Theory]</c>,
    /// <c>[Test]</c>, <c>[TestCase]</c>, <c>[TestMethod]</c>, …), so a helper placed before it is not run as the test; without one, the
    /// first <c>void</c> or <c>Task</c> method. Comments and string contents are not read. Null when there is no such method.
    /// </summary>
    public static string? MethodName(string reproduction)
    {
        string code = CSharpMask.Mask(reproduction);
        Match match = TestAttributedMethod().Match(code) is { Success: true } attributed ? attributed : AnyMethod().Match(code);
        return match.Success ? match.Groups["name"].Value : null;
    }

    private static async Task<ReviewFinding[]> RepairAsync(
        ReviewFinding[] judged,
        ReproductionCandidate[] candidates,
        Dictionary<int, TestApp> chosen,
        string root,
        string outputFolder,
        TimeSpan timeLimit,
        ReproductionRepairer repairer,
        IReadOnlySet<string> repairable,
        CancellationToken cancellationToken)
    {
        List<ReproductionCandidate> repaired = [];
        foreach (ReproductionCandidate candidate in candidates.Where(candidate => judged[candidate.Index] is { Reproduced: ReproductionResult.DidNotCompile, Carried: false } finding
            && repairable.Contains(finding.Id)))
        {
            ReproductionRepair repair = await repairer.RepairAsync(Request(judged[candidate.Index], chosen[candidate.Index], root), cancellationToken);
            if (!repair.Asked)
            {
                break;
            }

            judged[candidate.Index] = judged[candidate.Index] with { ReproductionAttempts = 2 };
            if (repair.Reproduction is string source)
            {
                repaired.Add(new ReproductionCandidate(candidate.Index, MethodName(source)!, source));
            }
        }

        if (repaired.Count == 0)
        {
            return judged;
        }

        Dictionary<int, string> sources = repaired.ToDictionary(candidate => candidate.Index, candidate => candidate.Source);
        foreach ((int index, ReproductionVerdict verdict) in await JudgeAsync(repaired, chosen, root, outputFolder, timeLimit, cancellationToken))
        {
            ReviewFinding finding = judged[index];
            judged[index] = verdict.Result switch
            {
                ReproductionResult.Failed => finding with { Outcome = FindingOutcome.Confirmed, Reproduced = ReproductionResult.Failed, Reproduction = sources[index] },
                ReproductionResult.Passed => finding with { Outcome = FindingOutcome.Unverified, Reproduced = ReproductionResult.RepairedPassed },
                _ => finding with
                {
                    Outcome = FindingOutcome.Unverified,
                    Reproduced = verdict.Result,
                    CompileErrors = verdict.Errors is null ? finding.CompileErrors : [.. finding.CompileErrors, verdict.Errors],
                },
            };
        }

        return judged;
    }

    private static ReproductionRepairRequest Request(ReviewFinding finding, TestApp app, string root)
    {
        string? project = TestProjectProfiles.ProjectFile(app.ProjectDirectory, app.AssemblyName);
        TestProjectProfile profile = project is null
            ? new TestProjectProfile(app.AssemblyName, TestProjectProfiles.Relative(root, app.ProjectDirectory), "unknown", "xUnit", [], [], TestProjectProfiles.Usings(app.ProjectDirectory))
            : TestProjectProfiles.Describe(project, root, app.Mode == RunnerMode.VSTest);
        return new ReproductionRepairRequest(
            finding, finding.CompileErrors.Count > 0 ? finding.CompileErrors[^1] : string.Empty, profile, CitedSignatures(finding.File, root), TestProjectProfiles.SampleTest(app.ProjectDirectory, root));
    }

    /// <summary>The test project a reproduction is built in, relative to the root: its project file, else its directory.</summary>
    private static string ProjectPath(TestApp app, string root) =>
        TestProjectProfiles.Relative(root, TestProjectProfiles.ProjectFile(app.ProjectDirectory, app.AssemblyName) ?? app.ProjectDirectory);

    /// <summary>The signatures of the cited C# file; empty when it is not a C# file inside the repository.</summary>
    private static string CitedSignatures(string file, string root)
    {
        string top = Path.GetFullPath(root);
        string cited = Path.GetFullPath(Path.Combine(top, file.Replace('/', Path.DirectorySeparatorChar)));
        bool inside = cited.StartsWith(top.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        return inside && cited.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && File.Exists(cited)
            ? CSharpSignatures.Extract(File.ReadAllText(cited), MaxSignatureCharacters)
            : string.Empty;
    }

    /// <summary>Builds and runs the candidates, each in its chosen test project; the verdict of every candidate by its index.</summary>
    private static async Task<Dictionary<int, ReproductionVerdict>> JudgeAsync(
        IReadOnlyList<ReproductionCandidate> candidates, Dictionary<int, TestApp> chosen, string root, string outputFolder, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        Dictionary<int, ReproductionVerdict> verdicts = [];
        foreach (IGrouping<TestApp, ReproductionCandidate> group in candidates.GroupBy(candidate => chosen[candidate.Index]))
        {
            List<string> usings = [.. TestProjectProfiles.Usings(group.Key.ProjectDirectory)];
            await JudgeInProjectAsync(group.Key, [.. group], usings, verdicts, root, outputFolder, timeLimit, cancellationToken);
        }

        return verdicts;
    }

    private static async Task JudgeInProjectAsync(
        TestApp app,
        List<ReproductionCandidate> pending,
        List<string> usings,
        Dictionary<int, ReproductionVerdict> verdicts,
        string root,
        string outputFolder,
        TimeSpan timeLimit,
        CancellationToken cancellationToken)
    {
        int usingRepairs = 0;
        string lastErrors = string.Empty;
        for (int round = pending.Count + MaxUsingRepairs + 1; round > 0 && pending.Count > 0; round--)
        {
            ReproductionBuildResult build = await ReproductionBuild.RunAsync(app, pending, usings, root, outputFolder, timeLimit, cancellationToken);
            if (build.Status != ReproductionBuildStatus.DidNotCompile)
            {
                foreach (ReproductionCandidate candidate in pending)
                {
                    verdicts[candidate.Index] = new ReproductionVerdict(
                        build.Status == ReproductionBuildStatus.Ran ? Judge(build.Results, ReproductionBuild.Key(app, candidate)) : ReproductionResult.NotRun, null);
                }

                return;
            }

            lastErrors = CompileErrors.Summary(build.Errors);
            int[] unresolved = [.. build.Errors.Select(error => ReproductionBuild.UsingAt(error, usings.Count)).OfType<int>().Distinct().OrderDescending()];
            if (unresolved.Length > 0 && usingRepairs++ < MaxUsingRepairs)
            {
                foreach (int index in unresolved)
                {
                    usings.RemoveAt(index);
                }

                continue;
            }

            ILookup<string, CompileError> byFile = build.Errors.ToLookup(error => error.File, StringComparer.OrdinalIgnoreCase);
            ReproductionCandidate[] broken = [.. pending.Where(candidate => byFile.Contains(candidate.FileName))];
            if (broken.Length > 0)
            {
                foreach (ReproductionCandidate candidate in broken)
                {
                    verdicts[candidate.Index] = new ReproductionVerdict(ReproductionResult.DidNotCompile, CompileErrors.Summary([.. byFile[candidate.FileName]]));
                }

                pending.RemoveAll(broken.Contains);
                continue;
            }

            // Errors no reproduction is named in (a stray brace, a clash with the project's code): each on its own.
            if (pending.Count > 1)
            {
                foreach (ReproductionCandidate candidate in pending)
                {
                    await JudgeInProjectAsync(app, [candidate], [.. usings], verdicts, root, outputFolder, timeLimit, cancellationToken);
                }

                return;
            }

            break;
        }

        foreach (ReproductionCandidate candidate in pending)
        {
            verdicts[candidate.Index] = new ReproductionVerdict(ReproductionResult.DidNotCompile, lastErrors);
        }
    }

    private static ReproductionResult Judge(IReadOnlyList<ExecutedTest> results, string key)
    {
        ExecutedTest[] rows = [.. results.Where(result => result.Key == key)];
        return rows.Length == 0 ? ReproductionResult.Inconclusive
            : rows.Any(row => row.Outcome == TestOutcome.Fail) ? ReproductionResult.Failed
            : rows.All(row => row.Outcome == TestOutcome.Pass) ? ReproductionResult.Passed
            : ReproductionResult.Inconclusive;
    }

    // An attribute list's contents may hold one level of brackets, such as the array in [InlineData(new[] { 1 })].
    [GeneratedRegex(
        @"\[(?:(?:[^\[\]]|\[[^\[\]]*\])*,)?\s*(?:\w+\s*\.\s*)*(?:Fact|Theory|Test|TestCase|TestCaseSource|TestMethod|DataTestMethod)(?:Attribute)?\s*(?:[(,](?:[^\[\]]|\[[^\[\]]*\])*)?\]"
        + @"(?:\s*\[(?:[^\[\]]|\[[^\[\]]*\])*\])*\s*(?:(?:public|internal|protected|private|static|async|override|virtual|new)\s+)*(?:void|Task|ValueTask)\s+(?<name>\w+)\s*\(")]
    internal static partial Regex TestAttributedMethod();

    [GeneratedRegex(@"\b(?:void|Task)\s+(?<name>\w+)\s*\(")]
    private static partial Regex AnyMethod();
}
