using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// The test files that hold a red's new tests and already existed at an earlier cycle's red (an earlier story's file, or one from
/// before the feature). The CI judge re-checks an earlier cycle's test that counts as changed since its red by building that
/// cycle's red commit with the head's test files laid over it (<c>OverlayRecheck</c>). A test file that existed at that red is laid
/// over whole, so the later tests in it come along, and when they use members the earlier red's code does not have, the build
/// fails and that cycle's tests since red are NOT-EARNED; a test file added since that holds only tests is left out. A file is
/// placed at an earlier red by the file hashes that red recorded (<see cref="RedGreenCycle.TestFiles"/>); for a cycle without
/// them (recorded before them, or a test project at the root), by the files that declare its tests (<see cref="TestSourceFiles"/>).
/// </summary>
internal static class SharedTestFiles
{
    public static IReadOnlyList<SharedTestFile> Find(string root, IReadOnlyList<string> testProjects, IReadOnlyList<RedGreenCycle> earlier, IReadOnlyList<TestCase> fresh)
    {
        if (earlier.Count == 0 || fresh.Count == 0)
        {
            return [];
        }

        (int Cycle, string ClassName, string Method)[] unhashedTests = [.. earlier
            .Where(cycle => cycle.TestFiles.Count == 0)
            .SelectMany(cycle => cycle.Tests.Select(test => (cycle.Number, Split: test.MethodFilter.LastIndexOf('.'), test.MethodFilter)))
            .Where(test => test.Split > 0)
            .Select(test => (test.Number, test.MethodFilter[..test.Split], test.MethodFilter[(test.Split + 1)..]))];
        IReadOnlyList<IReadOnlyList<string>> files = TestSourceFiles.FindAll(
            root, testProjects, [.. fresh.Select(test => (test.Class, test.Method)), .. unhashedTests.Select(test => (test.ClassName, test.Method))]);

        Dictionary<string, SortedSet<int>> declaredBy = new(StringComparer.Ordinal);
        for (int index = 0; index < unhashedTests.Length; index++)
        {
            foreach (string file in files[fresh.Count + index])
            {
                GetOrAdd(declaredBy, file).Add(unhashedTests[index].Cycle);
            }
        }

        SortedDictionary<string, (SortedSet<int> Cycles, List<string> NewTests)> shared = new(StringComparer.Ordinal);
        for (int index = 0; index < fresh.Count; index++)
        {
            foreach (string file in files[index])
            {
                SortedSet<int> cycles = [.. earlier.Where(cycle => cycle.TestFiles.ContainsKey(file)).Select(cycle => cycle.Number), .. declaredBy.GetValueOrDefault(file) ?? []];
                if (cycles.Count == 0)
                {
                    continue;
                }

                if (!shared.TryGetValue(file, out (SortedSet<int> Cycles, List<string> NewTests) entry))
                {
                    entry = (cycles, []);
                    shared[file] = entry;
                }

                entry.NewTests.Add(TraceMatrix.ShortName(fresh[index].MethodFilter));
            }
        }

        return [.. shared.Select(entry => new SharedTestFile(entry.Key, [.. entry.Value.Cycles], entry.Value.NewTests))];
    }

    /// <summary>What the agent reads: the files, why CI cares, and what to do whether or not this red is accepted.</summary>
    public static string Warning(IReadOnlyList<SharedTestFile> shared)
    {
        string files = string.Join("; ", shared.Select(file => $"{file.Path} (cycle {string.Join(", ", file.Cycles)}; new: {List(file.NewTests)})"));
        return string.Join('\n',
            $"Warning: new tests of this cycle are in test files that already existed at an earlier cycle's red: {files}.",
            "  Why: when an earlier cycle's test counts as changed after its red (its code, its class's constructor, fields or set-up, a helper it calls, "
            + "or its test project's other inputs: the project file, a helper or fixture file, a shared helper project, a runner setting), the CI judge re-checks it "
            + "on that red commit with this branch's test files laid over it. A test file that existed at that red is laid over whole, these new tests included; "
            + "where they use members that red commit does not have, the build fails and tests since red is NOT-EARNED. A test file added since, holding only tests, is left out.",
            "  What to do: put each user story's tests in new test files of their own. If this red is rejected, move the new tests into a new file before the next red. "
            + "If it is accepted, moving them would rename them, which CI counts as lost too; leave what the earlier cycle's tests depend on as it was at their red instead.");
    }

    private static SortedSet<int> GetOrAdd(Dictionary<string, SortedSet<int>> cyclesByFile, string file) =>
        cyclesByFile.TryGetValue(file, out SortedSet<int>? cycles) ? cycles : cyclesByFile[file] = [];

    private static string List(IReadOnlyList<string> tests) =>
        string.Join(", ", tests.Take(RedGreenSession.MaxTestsListed)) + (tests.Count > RedGreenSession.MaxTestsListed ? $" and {tests.Count - RedGreenSession.MaxTestsListed} more" : string.Empty);
}
