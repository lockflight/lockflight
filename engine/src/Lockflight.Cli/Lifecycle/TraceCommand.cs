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
/// <see cref="TraceMatrix"/>). Listing needs current assemblies, so trace builds (incrementally) and lists the tests; it runs none.
/// Prints a Markdown matrix for the PR body.
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

        IReadOnlyList<Requirement> requirements = SpecDocument.Parse(File.ReadAllText(feature.SpecPath)).Requirements;
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

        if (record is null)
        {
            await context.Output.WriteLineAsync($"NOT-EARNED {feature.Id} has no red/green evidence; trace needs 'lockflight redgreen' cycles");
            return ExitCode.NotEarned;
        }

        if (await CurrentInventoryAsync(manifest, feature, cancellationToken) is not IReadOnlyList<TestCase> inventory)
        {
            return ExitCode.NotEarned;
        }

        IReadOnlyList<TraceRow> rows = TraceMatrix.Build(requirements, record, inventory);
        await WriteMatrixAsync(feature, rows);
        await WriteNotesAsync(requirements, record);
        string[] lostLast = [.. rows.Where(row => !row.Covered && row.Lost.Count > 0).Select(row => row.RequirementId)];
        if (lostLast.Length > 0)
        {
            await context.Output.WriteLineAsync($"\n{string.Join(", ", lostLast)} lost every test that went red, then green (deleted, renamed or switched off since); a renamed test needs a new red/green cycle.");
        }

        int covered = rows.Count(row => row.Covered);
        await context.Output.WriteLineAsync($"\n{covered} of {requirements.Count} requirements covered by tests that went red, then green, and still exist.");
        return covered == requirements.Count ? ExitCode.Pass : ExitCode.Fail;
    }

    /// <summary>The Markdown matrix, after a warning for each approval that no longer covers the artifacts.</summary>
    private async Task WriteMatrixAsync(Feature feature, IReadOnlyList<TraceRow> rows)
    {
        await context.Output.WriteLineAsync($"### Requirement trace — {feature.Id}\n");
        ApprovalStore approvals = new(feature);
        ApprovalCheck[] checks = [approvals.Check(ApprovalPhase.Spec), approvals.Check(ApprovalPhase.Tasks)];
        foreach (ApprovalCheck unapproved in checks.Where(check => check.State is ApprovalState.Stale or ApprovalState.Invalid))
        {
            await context.Output.WriteLineAsync($"> **Warning:** {unapproved.Describe()}; this trace is against artifacts nobody approved.\n");
        }

        await context.Output.WriteLineAsync("| Requirement | Tests (red → green) | Status |");
        await context.Output.WriteLineAsync("|---|---|---|");
        foreach (TraceRow row in rows)
        {
            string tests = row.Covered ? string.Join("<br>", row.Tests) : "—";
            string status = row.Covered ? "covered" : row.Pending ? "red (cycle open)" : "**not covered**";
            string lost = row.Lost.Count > 0 ? $"<br>no longer counted: {string.Join(", ", row.Lost)}" : string.Empty;
            await context.Output.WriteLineAsync($"| {row.RequirementId} | {tests}{lost} | {status} |");
        }
    }

    /// <summary>Builds and lists the tests (none run); null — after saying why — when that is impossible, so trace cannot vouch.</summary>
    private async Task<IReadOnlyList<TestCase>?> CurrentInventoryAsync(Manifest manifest, Feature feature, CancellationToken cancellationToken)
    {
        await context.Error.WriteLineAsync("lockflight: building to list the current tests (no tests run)…");
        TestWorkspace workspace = TestWorkspaces.For(manifest, feature, context.Time);
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
