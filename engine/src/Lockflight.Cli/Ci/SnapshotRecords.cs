namespace Lockflight.Cli.Ci;

/// <summary>
/// The record mode of snapshot tests: a test helper that writes its case files only when a snapshot variable asks for it
/// (<c>if (Environment.GetEnvironmentVariable("SHOP_SNAPSHOTS") == "record") { Directory.CreateDirectory(…); File.WriteAllText(…); }</c>)
/// is not a host-tampering finding. A write is such a record when all of these hold, read from the method's IL; anything else
/// stays a finding:
/// <list type="bullet">
/// <item>it is <c>File.WriteAll*</c> or <c>Directory.CreateDirectory</c> of the runtime's own assemblies: no delete, move, copy,
/// append or stream;</item>
/// <item>it lies inside an <c>if</c> on <c>Environment.GetEnvironmentVariable("&lt;name&gt;") == "&lt;literal&gt;"</c> (or
/// <c>string.Equals</c>), branching on that comparison itself, with nothing jumping into the block from outside the guard (no
/// <c>||</c> around it, no <c>await</c> inside it); unset, the variable is null, equals no literal, and the block does not run;</item>
/// <item>the name contains <c>SNAPSHOT</c>, does not start like a CI runner's variables, and appears in the assembly only where a
/// guard reads it, and nowhere in the repository outside the test code (a runsettings, MSBuild or launch settings file, production
/// code, a workflow), which could set it;</item>
/// <item>the method reads no other variable and names no folder of the run (current, base or process directory, an assembly's
/// location), no runner's results, parent (<c>..</c>) or rooted path, and no file the test host or build reads (sources,
/// project and build files, runner and app configuration), itself or through a member of the assembly it uses.</item>
/// </list>
/// A guardrail: the strings are literals the assembly loads, so a path a caller builds at run time is not seen, and an
/// obfuscated call (a name or path assembled from pieces) evades it.
/// </summary>
internal static class SnapshotRecords
{
    private const string SnapshotMark = "SNAPSHOT";
    private const string GetVariable = "System.Environment::GetEnvironmentVariable";

    private static readonly string[] RecordWrites =
    [
        "System.IO.File::WriteAllText", "System.IO.File::WriteAllTextAsync", "System.IO.File::WriteAllBytes", "System.IO.File::WriteAllBytesAsync",
        "System.IO.File::WriteAllLines", "System.IO.File::WriteAllLinesAsync", "System.IO.Directory::CreateDirectory",
    ];

    private static readonly string[] Comparisons = ["System.String::op_Equality", "System.String::Equals"];

    /// <summary>Prefixes of variables CI systems set, besides those the scanner's CI-variable check names.</summary>
    private static readonly string[] MoreCiPrefixes = ["GITLAB_", "AGENT_", "TEAMCITY_", "RELEASE_", "TF_", "JENKINS_", "BUILDKITE_", "CIRCLE_", "TRAVIS_", "APPVEYOR_"];

    /// <summary>Endings of the files the build or the test host reads: sources, project and build files, configuration, binaries, scripts.</summary>
    private static readonly string[] HostFileEndings =
    [
        ".cs", ".fs", ".vb", ".csx", ".fsx", ".razor", ".cshtml", ".resx", ".xaml", ".tt", ".proto", ".csproj", ".fsproj", ".vbproj", ".proj",
        ".props", ".targets", ".sln", ".slnx", ".slnf", ".rsp", ".runsettings", ".testsettings", ".config", ".editorconfig", ".globalconfig",
        ".ruleset", ".dll", ".exe", ".pdb", ".so", ".dylib", ".sh", ".ps1", ".cmd", ".bat", ".yml", ".yaml",
    ];

    /// <summary>Names of the files the build or the test host reads or reports, wherever they appear in a string.</summary>
    private static readonly string[] HostFileNames =
    [
        "xunit.runner", "testconfig.json", "global.json", "nuget.config", "Directory.Build.", "Directory.Packages.", ".runtimeconfig.",
        ".deps.json", "project.assets.json", "appsettings", "launchSettings", "packages.lock.json", ".gitattributes", ".gitignore",
        ".lockflight", "TestResult", "coverage", "cobertura",
    ];

    /// <summary>
    /// Whether the write call at <paramref name="index"/> of <paramref name="body"/> is a record-mode snapshot write, given the
    /// body's guards (<see cref="Guards"/>), the members that name another folder of the run with the guards' reads left out
    /// (<paramref name="elsewhere"/>), and the members that name a file the host reads (<paramref name="hostFiles"/>).
    /// </summary>
    public static bool IsRecord(TamperingBody body, int index, IReadOnlyList<RecordGuard> guards, IReadOnlySet<string> elsewhere, IReadOnlySet<string> hostFiles)
    {
        int offset = body.CallOffsets[index];
        return body.Steps.Any(step => step.Offset == offset && step.Kind == TamperingStepKind.Call && RecordWrites.Contains(step.Text, StringComparer.Ordinal))
            && !body.Strings.Concat(body.AttributeStrings).Any(NamesAHostFile)
            && !elsewhere.Contains(body.Key) && !body.Members.Overlaps(elsewhere)
            && !hostFiles.Contains(body.Key) && !body.Members.Overlaps(hostFiles)
            && ReadsOnlyGuards(body, guards)
            && guards.Any(guard => offset >= guard.Start && offset < guard.End);
    }

    /// <summary>
    /// The method with the variable reads of its record-mode guards left out, when those are its only reads: what the scan for
    /// folders of the run looks at, so the guard does not count as naming one.
    /// </summary>
    public static TamperingBody WithoutGuardReads(TamperingBody body, IReadOnlyList<RecordGuard> guards)
    {
        if (guards.Count == 0 || !ReadsOnlyGuards(body, guards))
        {
            return body;
        }

        HashSet<int> reads = [.. guards.Select(guard => guard.Read)];
        int[] kept = [.. Enumerable.Range(0, body.Calls.Count).Where(index => !reads.Contains(body.CallOffsets[index]))];
        return body with { Calls = [.. kept.Select(index => body.Calls[index])], CallOffsets = [.. kept.Select(index => body.CallOffsets[index])] };
    }

    /// <summary>
    /// The strings the assembly uses other than as the name a guard reads: an attribute's, or an <c>ldstr</c> not followed at once
    /// by <c>Environment.GetEnvironmentVariable</c>. A snapshot variable among them may be set by the assembly itself (a helper,
    /// a field, a lambda passing it to <c>SetEnvironmentVariable</c>), so its guards do not count.
    /// </summary>
    public static HashSet<string> SwitchedOn(IEnumerable<TamperingBody> bodies)
    {
        HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);
        foreach (TamperingBody body in bodies)
        {
            used.UnionWith(body.AttributeStrings);
            for (int index = 0; index < body.Steps.Count; index++)
            {
                if (body.Steps[index].Kind == TamperingStepKind.String
                    && (index + 1 >= body.Steps.Count || body.Steps[index + 1] is not { Kind: TamperingStepKind.Call, Text: GetVariable }))
                {
                    used.Add(body.Steps[index].Text);
                }
            }
        }

        return used;
    }

    /// <summary>Whether a string names a file the build or the test host reads.</summary>
    public static bool NamesAHostFile(string text)
    {
        string trimmed = text.Trim();
        return HostFileEndings.Any(ending => trimmed.EndsWith(ending, StringComparison.OrdinalIgnoreCase))
            || HostFileNames.Any(name => trimmed.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether a variable name is one a record-mode guard may read: see the class summary. <paramref name="isSet"/> says whether something else may set it.</summary>
    public static bool IsSnapshotVariable(string name, Func<string, bool> isSet) =>
        name.Contains(SnapshotMark, StringComparison.OrdinalIgnoreCase)
        && !HostTamperingScanner.CiVariablePrefixes.Concat(MoreCiPrefixes).Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        && !isSet(name);

    /// <summary>
    /// The method's record-mode guards: <c>ldstr name; call GetEnvironmentVariable; ldstr literal; [ldc.i4]; call op_Equality or
    /// String.Equals; [stloc.N; ldloc.N]; brfalse forward</c>, the shape the C# compiler gives <c>if (… == "record")</c> in Debug and
    /// Release builds, where nothing outside the guard jumps past its <c>ldstr</c> into the block (a <c>||</c>, an <c>else</c>
    /// threaded into it, an exception handler, an <c>await</c>'s resumption).
    /// </summary>
    public static IReadOnlyList<RecordGuard> Guards(TamperingBody body, Func<string, bool> isSet)
    {
        List<TamperingStep> steps = body.Steps;
        List<RecordGuard> guards = [];
        for (int index = 0; index + 4 < steps.Count; index++)
        {
            if (steps[index] is not { Kind: TamperingStepKind.String } name
                || steps[index + 1] is not { Kind: TamperingStepKind.Call, Text: GetVariable } read
                || steps[index + 2] is not { Kind: TamperingStepKind.String }
                || !IsSnapshotVariable(name.Text, isSet))
            {
                continue;
            }

            int next = index + 3;
            while (next < steps.Count && steps[next].Kind == TamperingStepKind.Constant)
            {
                next++;
            }

            if (next >= steps.Count || steps[next].Kind != TamperingStepKind.Call || !Comparisons.Contains(steps[next].Text, StringComparer.Ordinal))
            {
                continue;
            }

            next++;
            if (next + 1 < steps.Count && steps[next] is { Kind: TamperingStepKind.StoreLocal } store
                && steps[next + 1] is { Kind: TamperingStepKind.LoadLocal } load && load.Target == store.Target)
            {
                next += 2;
            }

            if (next < steps.Count && steps[next] is { Kind: TamperingStepKind.BranchFalse } branch && branch.Target > branch.Next
                && !EnteredFromOutside(body, name.Offset, branch.Target))
            {
                guards.Add(new RecordGuard(name.Text, read.Offset, branch.Next, branch.Target));
            }
        }

        return guards;
    }

    /// <summary>Whether a jump from outside [<paramref name="first"/>, <paramref name="end"/>) lands after <paramref name="first"/> and before <paramref name="end"/>.</summary>
    private static bool EnteredFromOutside(TamperingBody body, int first, int end) =>
        body.Jumps.Any(jump => (jump.From < first || jump.From >= end) && jump.Target > first && jump.Target < end);

    /// <summary>Whether every variable the method reads is read by one of its guards.</summary>
    private static bool ReadsOnlyGuards(TamperingBody body, IReadOnlyList<RecordGuard> guards)
    {
        HashSet<int> reads = [.. guards.Select(guard => guard.Read)];
        return body.Calls.Select((call, index) => (call, index))
            .Where(entry => entry.call is ("System.Environment", "GetEnvironmentVariable" or "GetEnvironmentVariables" or "ExpandEnvironmentVariables"))
            .All(entry => reads.Contains(body.CallOffsets[entry.index]));
    }
}
