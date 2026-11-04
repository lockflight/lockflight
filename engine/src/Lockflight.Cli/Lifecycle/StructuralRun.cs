using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Lifecycle.RedGreen.Snapshots;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// Local trace's structural check (<see cref="StructuralCheck"/>): runs the structural tests in the working tree, then builds the
/// feature's start commit in a temporary worktree with the files that declare them and every non-C# file of the test projects laid
/// over it, runs them there and classifies each as a red's tests are. The CI judge does the same on the fork point. Structural tests
/// that share a file with tests needing newer code do not build there, so they belong in files of their own.
/// </summary>
internal sealed class StructuralRun(TestWorkspace workspace, string root, TimeProvider time, TimeSpan timeLimit)
{
    private static readonly HashSet<string> BuildOutput = new(["bin", "obj"], StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<StructuralTest>> RunAsync(IReadOnlyList<TestCase> tests, string startCommit, IReadOnlyList<string> testProjects, CancellationToken cancellationToken)
    {
        if (await workspace.RunAsync(tests, cancellationToken) is not IReadOnlyList<ExecutedTest> atHead)
        {
            return [.. tests.Select(test => Result(test, StructuralOutcome.FailsAtHead, $"the test runner produced no report for {workspace.FailedApp}"))];
        }

        TestCase[] passing = [.. tests.Where(test => SuiteChecks.PassedEveryRow(atHead, test.Key))];
        IReadOnlyDictionary<string, (RedClass? Class, bool EveryRowPassed, string Why)> onStart = passing.Length == 0
            ? new Dictionary<string, (RedClass?, bool, string)>(StringComparer.Ordinal)
            : await OnStartAsync(passing, startCommit, testProjects, cancellationToken);
        return [.. tests.Select(test => onStart.TryGetValue(test.Key, out (RedClass? Class, bool EveryRowPassed, string Why) start)
            ? Result(test, StructuralCheck.Classify(passesAtHead: true, start.Class, start.EveryRowPassed), start.Why)
            : Result(test, StructuralOutcome.FailsAtHead, "fails in the working tree"))];
    }

    private static StructuralTest Result(TestCase test, StructuralOutcome outcome, string why) => new(test.Key, test.MethodFilter, test.Requirements, outcome, why);

    private async Task<IReadOnlyDictionary<string, (RedClass? Class, bool EveryRowPassed, string Why)>> OnStartAsync(
        TestCase[] tests, string startCommit, IReadOnlyList<string> testProjects, CancellationToken cancellationToken)
    {
        if (startCommit.Length == 0)
        {
            return Unchecked(tests, "red/green recorded no start commit to check them on");
        }

        SnapshotWorktreeOpening opening = await SnapshotWorktree.OpenAsync(root, startCommit, timeLimit, cancellationToken);
        if (opening.Worktree is not SnapshotWorktree worktree)
        {
            return Unchecked(tests, opening.Problem ?? "the start commit could not be checked out");
        }

        try
        {
            if (Overlay(tests, testProjects, worktree.Root) is string notCopied)
            {
                return Unchecked(tests, notCopied);
            }

            TestWorkspace onStart = workspace.MovedTo(worktree.Root);
            if (await onStart.BuildAsync(cancellationToken) is not null)
            {
                return Unchecked(tests, $"their files do not build on the start commit {RedGreenSession.Short(startCommit)}'s code (keep structural tests in files of their own, using only APIs that code has)");
            }

            if (onStart.DiscoveryProblem is not null || await onStart.ListAsync(cancellationToken) is not IReadOnlyList<TestCase> inventory)
            {
                return Unchecked(tests, $"the tests on the start commit {RedGreenSession.Short(startCommit)}'s code could not be listed");
            }

            HashSet<string> keys = [.. tests.Select(test => test.Key)];
            TestCase[] runnable = [.. inventory.Where(test => keys.Contains(test.Key))];
            if (runnable.Length == 0 || await onStart.RunAsync(runnable, cancellationToken) is not IReadOnlyList<ExecutedTest> results)
            {
                return Unchecked(tests, $"they did not run on the start commit {RedGreenSession.Short(startCommit)}'s code");
            }

            RedEvaluationResult evaluation = await RedEvaluation.ClassifyAsync(onStart, worktree.Root, inventory, runnable, results, time.GetUtcNow().Add(timeLimit), time, cancellationToken);
            Dictionary<string, (RedClass? Class, bool EveryRowPassed, string Why)> classified = new(Unchecked(tests, "not among the tests on the start commit's code"), StringComparer.Ordinal);
            foreach (RedGreenTest test in evaluation.Classified)
            {
                string why = evaluation.Verdicts.TryGetValue(test.Key, out RedVerdict? verdict) ? verdict.Why : RedClassNames.ToName(test.RedClass);
                classified[test.Key] = (test.RedClass, SuiteChecks.PassedEveryRow(results, test.Key), $"{RedClassNames.ToName(test.RedClass)} on the start commit {RedGreenSession.Short(startCommit)}'s code: {why}");
            }

            return classified;
        }
        finally
        {
            await worktree.RemoveAsync();
        }
    }

    /// <summary>
    /// Lays the files that declare the structural tests and every non-C# file of the test projects (project files, snapshots,
    /// expected-value data; build output left out) over the worktree; why not, when a path there is a symbolic link or a copy fails.
    /// </summary>
    private string? Overlay(TestCase[] tests, IReadOnlyList<string> testProjects, string target)
    {
        IEnumerable<string> sources = TestSourceFiles.FindAll(root, testProjects, [.. tests.Select(test => (test.Class, test.Method))]).SelectMany(paths => paths);
        IEnumerable<string> inputs = testProjects
            .Select(project => project is "." or "" ? root : Path.Combine(root, project.Replace('/', Path.DirectorySeparatorChar)))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !path.Split('/').Any(BuildOutput.Contains));
        try
        {
            foreach (string path in sources.Concat(inputs).Distinct(StringComparer.Ordinal))
            {
                string destination = Path.Combine(target, path);
                if (new FileInfo(Path.Combine(root, path)).LinkTarget is not null || LinkOnTheWay(target, destination))
                {
                    return $"{path} is or lies under a symbolic link, which is not laid over the start commit's code";
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(root, path), destination, overwrite: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"the structural tests' files could not be laid over the start commit's code: {exception.Message}";
        }

        return null;
    }

    /// <summary>Whether the destination, or a directory between it and the worktree, is a symbolic link in the checked-out commit.</summary>
    private static bool LinkOnTheWay(string worktree, string destination)
    {
        for (string? path = destination; path is not null && path.Length > worktree.Length; path = Path.GetDirectoryName(path))
        {
            FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            if (entry.Exists && entry.LinkTarget is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, (RedClass? Class, bool EveryRowPassed, string Why)> Unchecked(TestCase[] tests, string why) =>
        tests.ToDictionary(test => test.Key, _ => ((RedClass?)null, false, why), StringComparer.Ordinal);
}
