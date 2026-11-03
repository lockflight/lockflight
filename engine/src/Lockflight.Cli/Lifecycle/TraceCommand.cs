using Lockflight.Cli.Config;
using Lockflight.Cli.Gates.Baselines;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.Approvals;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Lifecycle;

/// <summary>
/// <c>lockflight trace [--feature id]</c> — trace-lite: every requirement of the feature's spec must be named by a
/// <c>Requirement</c> trait on a test that went red and then green in one of <em>this feature's</em> cycles (spec FR numbers repeat
/// across features, so other features' tests never count), and that test must still be in the current inventory (see
/// <see cref="TraceMatrix"/>). Listing needs current assemblies, so trace builds (incrementally) and lists the tests; it runs only structural tests.
/// Prints a Markdown matrix for the PR body. A requirement marked <c>[structural]</c> is covered by a test naming only structural
/// requirements that passes here and on the start commit's code (<see cref="StructuralCheck"/>), which trace runs. A requirement marked <c>[outside .NET: …]</c> whose files name it is excluded — neither
/// covered nor failing — and when every requirement is, trace is N/A (exit 5), never a pass.
/// </summary>
internal sealed class TraceCommand(CliContext context) : ICommand
{
    private const int MaxRequirementsPerTest = 3;

    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        if (await CommandOptions.ParseOrReportAsync(arguments, context.Error, cancellationToken) is not CommandOptions options
            || await ManifestRequirement.LoadAsync(context, context.WorkingDirectory, cancellationToken) is not Manifest manifest)
        {
            return ExitCode.Config;
        }

        FeatureLookup lookup = FeatureLocator.Resolve(manifest.Root, options.Value("--feature"));
        if (lookup.Feature is not Feature feature)
        {
            await context.Error.WriteLineAsync($"lockflight: {lookup.Error}");
            return ExitCode.Config;
        }

        if (!File.Exists(feature.SpecPath))
        {
            await context.Error.WriteLineAsync($"lockflight: {Path.GetRelativePath(manifest.Root, feature.SpecPath)} does not exist; write the spec first");
            return ExitCode.Config;
        }

        SpecDocument spec = SpecDocument.Parse(File.ReadAllText(feature.SpecPath));
        IReadOnlyList<Requirement> requirements = spec.Requirements;
        RedGreenRecord? record = RedGreenRecord.Load(RedGreenRecord.PathFor(feature));
        if (record is null && new AdoptedFeatures(manifest.Root).Load().Contains(feature.Id))
        {
            await context.Output.WriteLineAsync($"ADOPTED {feature.Id} predates Lockflight: no red/green evidence is expected");
            return ExitCode.Pass;
        }

        if (requirements.Count == 0)
        {
            await context.Output.WriteLineAsync($"NOT-EARNED {Path.GetRelativePath(manifest.Root, feature.SpecPath)} defines no requirements (FR-001 …); there is nothing to trace");
            return ExitCode.NotEarned;
        }

        if (spec.DuplicateMarkedIds.Count > 0)
        {
            await context.Output.WriteLineAsync($"NOT-EARNED {Path.GetRelativePath(manifest.Root, feature.SpecPath)} defines {string.Join(", ", spec.DuplicateMarkedIds)} more than once, with a marker on one line; give each requirement one line");
            return ExitCode.NotEarned;
        }

        Dictionary<string, IReadOnlyList<OutsideFile>> outside = requirements.Where(requirement => requirement.VerifiedOutside.Count > 0).DistinctBy(requirement => requirement.Id)
            .ToDictionary(requirement => requirement.Id, requirement => OutsideVerification.ReadLocal(requirement, manifest.Root), StringComparer.Ordinal);
        if (record is null)
        {
            TraceRow[] outsideOnly = [.. requirements.Select(requirement => new TraceRow(requirement.Id, [], [], Pending: false) { Outside = outside.GetValueOrDefault(requirement.Id) ?? [] })];
            if (TraceTally.Of(outsideOnly).Outcome == TraceOutcome.NotApplicable)
            {
                await WriteMatrixAsync(feature, outsideOnly, manifest.Root);
                return await NotApplicableAsync(feature, requirements.Count);
            }

            await context.Output.WriteLineAsync($"NOT-EARNED {feature.Id} has no red/green evidence; trace needs 'lockflight redgreen' cycles");
            return ExitCode.NotEarned;
        }

        TestWorkspace workspace = TestWorkspaces.For(manifest, feature, context.Time);
        if (await CurrentInventoryAsync(workspace, cancellationToken) is not IReadOnlyList<TestCase> inventory)
        {
            return ExitCode.NotEarned;
        }

        IReadOnlyList<StructuralTest> structural = await StructuralAsync(manifest, workspace, requirements, record, inventory, cancellationToken);
        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(requirements, record, inventory, outside, structural);
        await WriteMatrixAsync(feature, rows, manifest.Root);
        await WriteNotesAsync(requirements, record);
        string[] lostLast = [.. rows.Where(row => !row.Covered && row.Lost.Count > 0).Select(row => row.RequirementId)];
        if (lostLast.Length > 0)
        {
            await context.Output.WriteLineAsync($"\n{string.Join(", ", lostLast)} lost every test that went red, then green (deleted, renamed or switched off since); a renamed test needs a new red/green cycle.");
        }

        string[] alsoDotNet = [.. rows.Where(row => row.Covered && row.Outside.Count > 0).Select(row => row.RequirementId)];
        if (alsoDotNet.Length > 0)
        {
            await context.Output.WriteLineAsync($"\nNote: {string.Join(", ", alsoDotNet)} {(alsoDotNet.Length == 1 ? "is" : "are")} marked [outside .NET: …] but named by a .NET test that went red, then green: counted as covered by that test.");
        }

        TraceTally tally = TraceTally.Of(rows);
        if (tally.Outcome == TraceOutcome.NotApplicable)
        {
            return await NotApplicableAsync(feature, requirements.Count);
        }

        string excluded = tally.Excluded > 0
            ? $"; {tally.Excluded} verified outside .NET by the files their marker names (Lockflight runs none of those tests: check them in review)"
            : string.Empty;
        await context.Output.WriteLineAsync($"\n{tally.Covered} of {requirements.Count} requirements covered by tests that went red, then green, and still exist{excluded}.");
        return tally.Outcome == TraceOutcome.Pass ? ExitCode.Pass : ExitCode.Fail;
    }

    /// <summary>Every requirement verified outside .NET: trace has no .NET test to vouch for, which is never a pass.</summary>
    private async Task<ExitCode> NotApplicableAsync(Feature feature, int requirements)
    {
        await context.Output.WriteLineAsync(
            $"\nN/A all {requirements} requirements of {feature.Id} are verified outside .NET by the files their marker names; Lockflight runs none of those tests, so trace has nothing to vouch for (check them in review).");
        return ExitCode.NotEarned;
    }

    /// <summary>The Markdown matrix, after a warning for each approval that no longer covers the artifacts.</summary>
    private async Task WriteMatrixAsync(Feature feature, IReadOnlyList<TraceRow> rows, string root)
    {
        await context.Output.WriteLineAsync($"### Requirement trace — {feature.Id}\n");
        ApprovalStore approvals = new(feature);
        ApprovalCheck[] checks = [approvals.Check(ApprovalPhase.Spec), approvals.Check(ApprovalPhase.Tasks)];
        foreach (ApprovalCheck unapproved in checks.Where(check => check.State is ApprovalState.Stale or ApprovalState.Invalid))
        {
            await context.Output.WriteLineAsync($"> **Warning:** {unapproved.Describe(root)}; this trace is against artifacts nobody approved.\n");
        }

        await context.Output.WriteLineAsync("| Requirement | Tests (red → green) | Status |");
        await context.Output.WriteLineAsync("|---|---|---|");
        foreach (TraceRow row in rows)
        {
            string tests = row.Covered ? string.Join("<br>", row.Tests) : "—";
            string status = row.Covered ? "covered"
                : row.Excluded ? $"excluded — {row.Note}"
                : row.Pending ? "red (cycle open)"
                : row.Note is string problem ? $"**not covered** ({problem})"
                : "**not covered**";
            string lost = row.Lost.Count > 0 ? $"<br>no longer counted: {string.Join(", ", row.Lost)}" : string.Empty;
            await context.Output.WriteLineAsync($"| {row.RequirementId} | {tests}{lost} | {status} |");
        }
    }

    /// <summary>Builds and lists the tests (none run); null — after saying why — when that is impossible, so trace cannot vouch.</summary>
    private async Task<IReadOnlyList<TestCase>?> CurrentInventoryAsync(TestWorkspace workspace, CancellationToken cancellationToken)
    {
        await context.Error.WriteLineAsync("lockflight: building to list the current tests…");
        if (await workspace.BuildAsync(cancellationToken) is not null)
        {
            await context.Output.WriteLineAsync("NOT-EARNED the build fails, so the current tests cannot be listed; trace re-checks red/green tests against them. Fix the build, then run trace again.");
            return null;
        }

        if (workspace.DiscoveryProblem is string incomplete)
        {
            await context.Output.WriteLineAsync($"NOT-EARNED the current tests cannot all be listed: {incomplete}");
            return null;
        }

        if (workspace.Apps.Count == 0)
        {
            await context.Output.WriteLineAsync("NOT-EARNED no test project found to list the current tests from (xUnit v3, xUnit v2 on VSTest, NUnit or MSTest)");
            return null;
        }

        if (await workspace.ListAsync(cancellationToken) is not IReadOnlyList<TestCase> inventory)
        {
            await context.Output.WriteLineAsync($"NOT-EARNED the test runner could not list the tests of {workspace.FailedApp}");
            return null;
        }

        return inventory;
    }

    /// <summary>
    /// The structural tests (naming only <c>[structural]</c> requirements), run in the working tree and on the start commit's code
    /// (<see cref="StructuralRun"/>); none when the spec marks no requirement structural.
    /// </summary>
    private async Task<IReadOnlyList<StructuralTest>> StructuralAsync(
        Manifest manifest, TestWorkspace workspace, IReadOnlyList<Requirement> requirements, RedGreenRecord record, IReadOnlyList<TestCase> inventory, CancellationToken cancellationToken)
    {
        IReadOnlySet<string> structuralIds = StructuralCheck.Ids(requirements);
        // A test the start commit already had (another feature's among them) proves nothing about this change.
        TestCase[] tests = [.. inventory.Where(test => StructuralCheck.IsStructural(test.Requirements, structuralIds) && test.SkipReason is null && !test.IsExplicit
            && !record.BaselineTests.Contains(test.Key))];
        if (tests.Length == 0)
        {
            return [];
        }

        await context.Error.WriteLineAsync($"lockflight: running {tests.Length} structural tests here and on the start commit {RedGreenSession.Short(record.BaselineCommit)}'s code…");
        string[] testProjects = [.. record.TestProjects.Union(workspace.Apps.Select(app => Path.GetRelativePath(manifest.Root, app.ProjectDirectory).Replace('\\', '/')), StringComparer.Ordinal)];
        return await new StructuralRun(workspace, manifest.Root, context.Time, TimeSpan.FromSeconds(manifest.Gates.TimeoutSeconds))
            .RunAsync(tests, record.BaselineCommit, testProjects, cancellationToken);
    }

    private async Task WriteNotesAsync(IReadOnlyList<Requirement> requirements, RedGreenRecord record)
    {
        RedGreenTest[] all = [.. record.Cycles.SelectMany(cycle => cycle.Tests)];
        HashSet<string> known = [.. requirements.Select(requirement => requirement.Id)];
        string[] unknown = [.. all.SelectMany(test => test.Requirements).Where(id => !known.Contains(id)).Distinct().Order(StringComparer.Ordinal)];
        if (unknown.Length > 0)
        {
            await context.Output.WriteLineAsync($"\nTests name requirements this spec does not define: {string.Join(", ", unknown)}");
        }

        string[] assertionFree = [.. record.Cycles.Where(cycle => !cycle.IsOpen).SelectMany(cycle => cycle.Tests).Where(test => test.AssertionFree).Select(test => TraceMatrix.ShortName(test.MethodFilter))];
        if (assertionFree.Length > 0)
        {
            await context.Output.WriteLineAsync(
                $"\nNote: {string.Join(", ", assertionFree)} assert nothing ({RedGreenTest.AssertionFreeMark}): they count, but pass whenever the code returns without throwing; check in review that each verifies its requirement.");
        }

        if (RedGreenAmends.Lines(record) is { Count: > 0 } amends)
        {
            await context.Output.WriteLineAsync($"\nNote: {amends.Count} {(amends.Count == 1 ? "test was changed after its red" : "tests were changed after their red")}, each with a person's amend; check in review that the change kept what the test verifies:");
            foreach (string amend in amends)
            {
                await context.Output.WriteLineAsync($"- {amend}");
            }
        }

        foreach (RedGreenTest broad in record.Cycles.Where(cycle => !cycle.IsOpen).SelectMany(cycle => cycle.Tests).Where(test => test.Requirements.Count > MaxRequirementsPerTest))
        {
            await context.Output.WriteLineAsync($"\nNote: {TraceMatrix.ShortName(broad.MethodFilter)} claims {broad.Requirements.Count} requirements; check that it really verifies each one.");
        }
    }
}
