using Lockflight.Cli.Canaries;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Hooks.Guard;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Tests.Canaries;

/// <summary>
/// Every registered control has a canary in front of it that it catches: each guard rule (<see cref="GuardRules"/>), each gate step
/// (<see cref="GateProfiles"/>) and each command reserved for a person (<see cref="CommandRegistry"/>). A control registered
/// without one fails here, with the rule CONTRIBUTING.md states.
/// </summary>
[Trait(CanarySuite.Trait, CanarySuite.TraitValue)]
public sealed class CanaryCoverageTests
{
    public const string MissingGateStepCanary =
        "no violation is planted for this gate step: a step registered in GateProfiles needs a canary in "
        + "engine/tests/Lockflight.Cli.Tests/Canaries/GateStepCanaries.cs in the same pull request (CONTRIBUTING.md, Canaries)";

    public const string MissingGuardRuleCanary =
        "no violation is planted for this guard rule: a rule registered in GuardRules needs a canary in "
        + "engine/tests/Lockflight.Cli.Tests/Canaries/GuardCanaries.cs in the same pull request (CONTRIBUTING.md, Canaries)";

    public const string MissingHumanOnlyCanary =
        "no arguments are planted for this command: a command registered for CommandAudience.Human needs an entry in "
        + "engine/tests/Lockflight.Cli.Tests/Canaries/HumanOnlyCanaries.cs in the same pull request (CONTRIBUTING.md, Canaries)";

    [Fact]
    public async Task EveryGuardRuleHasACanaryItCatches()
    {
        using CanaryScratch scratch = new(TimeProvider.System);
        CliContext context = new(new StringReader(string.Empty), TextWriter.Null, TextWriter.Null, scratch.Project, TimeProvider.System);
        string[] rules = [.. GuardRules.OncePerCall.Concat(GuardRules.PerRoot(context, new DirectoryReads())).Select(rule => rule.Id)];

        IReadOnlyList<CanaryReport> reports = await GuardCanaries.RunAsync(scratch, TestContext.Current.CancellationToken);

        CanarySuite.AssertAllCaught(reports);
        Assert.Equal(rules.Length, rules.Distinct(StringComparer.Ordinal).Count());
        foreach (string rule in rules)
        {
            Assert.True(reports.Any(report => report.Control == rule && report.Result == CanaryResult.Caught), $"guard rule '{rule}': {MissingGuardRuleCanary}");
        }
    }

    [Fact]
    public async Task EveryGateStepHasACanaryItCatches()
    {
        string[] steps = [.. GateProfiles.All.SelectMany(definition => definition.InOrder).Select(step => step.Name).Distinct(StringComparer.Ordinal)];

        IReadOnlyList<CanaryReport> reports = await GateStepCanaries.RunAsync(TimeProvider.System, TestContext.Current.CancellationToken);

        CanarySuite.AssertAllCaught(reports);
        foreach (string step in steps)
        {
            CanaryReport? report = reports.SingleOrDefault(report => report.Control == step);
            Assert.True(report is not null, $"gate step '{step}': {MissingGateStepCanary}");
            Assert.True(report.Result == CanaryResult.Caught, $"gate step '{step}': {report.Result} {report.Name}: {report.Detail}");
        }
    }

    [Fact]
    public async Task EveryHumanOnlyCommandHasACanaryItCatches()
    {
        using CanaryScratch scratch = new(TimeProvider.System);
        string[] commands = [.. CommandRegistry.All.Where(command => command.Audience == CommandAudience.Human).Select(command => command.Name)];

        IReadOnlyList<CanaryReport> reports = await HumanOnlyCanaries.RunAsync(scratch, TestContext.Current.CancellationToken);

        CanarySuite.AssertAllCaught(reports);
        foreach (string command in commands)
        {
            CanaryReport report = Assert.Single(reports, report => report.Control == command);
            Assert.True(report.Result == CanaryResult.Caught, $"'{command}': {report.Result} {report.Detail}");
        }
    }
}
