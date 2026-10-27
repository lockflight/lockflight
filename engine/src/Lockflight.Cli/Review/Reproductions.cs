using System.Text;
using System.Text.RegularExpressions;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Review;

/// <summary>
/// Confirms blocker/major findings by running the reviewer's reproduction <c>[Fact]</c>s in a test project: a
/// reproduction that compiles and fails confirms the finding, one that passes rejects it, anything else leaves it unverified. Each
/// finding records what its reproduction did (<see cref="ReproductionResult"/>).
/// The generated file is removed afterwards (a hard kill can leave it behind). Building and running the reproductions executes the
/// project's own build logic and test fixtures — code the agent may have written — like any gate run.
/// </summary>
internal static partial class Reproductions
{
    public const string Namespace = "Lockflight.Reproductions";

    private const string FileName = "LockflightReproductions.g.cs";

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

    public static async Task<IReadOnlyList<ReviewFinding>> ConfirmAsync(
        IReadOnlyList<ReviewFinding> findings, TestApp app, string root, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        ReviewFinding[] judged = [.. findings.Select(Unjudged)];
        (int Index, string Method)[] candidates = [.. judged
            .Select((finding, index) => (finding, index))
            .Where(item => IsRunnable(item.finding))
            .Select(item => (item.index, MethodName(item.finding.Reproduction!)!))];
        if (candidates.Length == 0)
        {
            return judged;
        }

        string usings = Usings(app.ProjectDirectory);
        // All reproductions in one build first; when that does not compile, each on its own so one bad snippet does not hide the rest.
        IEnumerable<(int, string)[]> attempts = [candidates];
        foreach ((int Index, string Method)[] batch in attempts.Concat(candidates.Length > 1 ? candidates.Select(candidate => new[] { candidate }) : []))
        {
            if (batch.All(candidate => judged[candidate.Index].Reproduced != ReproductionResult.NotRun))
            {
                continue;
            }

            Dictionary<int, (FindingOutcome Outcome, ReproductionResult Result)>? results = await RunBatchAsync(judged, batch, usings, app, root, timeLimit, cancellationToken);
            if (results is null)
            {
                if (batch.Length == 1)
                {
                    judged[batch[0].Index] = judged[batch[0].Index] with { Reproduced = ReproductionResult.DidNotCompile };
                }

                continue;
            }

            foreach ((int index, (FindingOutcome outcome, ReproductionResult result)) in results)
            {
                judged[index] = judged[index] with { Outcome = outcome, Reproduced = result };
            }

            if (batch.Length == candidates.Length)
            {
                break;
            }
        }

        return judged;
    }

    private static async Task<Dictionary<int, (FindingOutcome Outcome, ReproductionResult Result)>?> RunBatchAsync(
        ReviewFinding[] findings, (int Index, string Method)[] batch, string usings, TestApp app, string root, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        string path = Path.Combine(app.ProjectDirectory, FileName);
        StringBuilder source = new(usings);
        source.AppendLine($"namespace {Namespace};").AppendLine();
        foreach ((int index, _) in batch)
        {
            source.AppendLine($"public sealed class Finding{index + 1}").AppendLine("{").AppendLine(findings[index].Reproduction).AppendLine("}").AppendLine();
        }

        try
        {
            await File.WriteAllTextAsync(path, source.ToString(), cancellationToken);
            ProcessResult build = await ProcessRunner.RunAsync(
                "dotnet", ["build", app.ProjectDirectory, "--nologo", "--interactive", "false", "-p:TreatWarningsAsErrors=false"], root, timeLimit, cancellationToken);
            if (build.ExitCode != 0)
            {
                return null;
            }

            string[] filters = [.. batch.Select(candidate => $"{Namespace}.Finding{candidate.Index + 1}.{candidate.Method}")];
            IReadOnlyList<ExecutedTest>? results = (await NativeTestRunner.RunAsync(app, filters, root, timeLimit, cancellationToken))?.Results;
            Dictionary<int, (FindingOutcome, ReproductionResult)> judged = [];
            foreach ((int index, string method) in batch)
            {
                ExecutedTest[] rows = [.. (results ?? []).Where(result => result.Key == $"{app.AssemblyName}:{Namespace}.Finding{index + 1}.{method}")];
                judged[index] = rows.Length == 0 ? (FindingOutcome.Unverified, ReproductionResult.Inconclusive)
                    : rows.Any(row => row.Outcome == TestOutcome.Fail) ? (FindingOutcome.Confirmed, ReproductionResult.Failed)
                    : rows.All(row => row.Outcome == TestOutcome.Pass) ? (FindingOutcome.Rejected, ReproductionResult.Passed)
                    : (FindingOutcome.Unverified, ReproductionResult.Inconclusive);
            }

            return judged;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string Usings(string projectDirectory)
    {
        IEnumerable<string> lines = Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.EndsWith(FileName, StringComparison.Ordinal))
            .SelectMany(File.ReadLines)
            .Select(line => line.Trim())
            .Where(line => UsingDirective().IsMatch(line));
        return string.Join('\n', lines.Append("using Xunit;").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)) + "\n\n";
    }

    public static string? MethodName(string reproduction) => TestMethod().Match(reproduction) is { Success: true } match ? match.Groups["name"].Value : null;

    [GeneratedRegex(@"^using\s+(static\s+)?[\w.]+(\s*=\s*[\w.<>]+)?\s*;$")]
    private static partial Regex UsingDirective();

    [GeneratedRegex(@"\b(?:void|Task)\s+(?<name>\w+)\s*\(")]
    private static partial Regex TestMethod();
}
