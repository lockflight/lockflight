using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.Approvals;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class SessionCommandSpecTests
{
    [Fact]
    public void EverySessionCommandCreatesItsOwnDeciderAndEveryTwoStepCommandOwnsDistinctCodeSlots()
    {
        CliContext context = new(new StringReader(string.Empty), TextWriter.Null, TextWriter.Null, Path.GetTempPath(), TimeProvider.System);
        Manifest manifest = new(Path.GetTempPath(), [], StopGate: false, StopTarget: null, GateSettings.Default);

        Assert.Equal(SessionCommands.All.Count, SessionCommands.All.Select(spec => spec.Create(context, manifest).GetType()).Distinct().Count());
        string[] slots = [.. SessionCommands.All.SelectMany(spec => spec.Slots).Select(slot => slot.Slot)];
        Assert.Equal(slots.Length, slots.Distinct(StringComparer.Ordinal).Count());
        Assert.All(SessionCommands.All.Where(spec => spec != SessionApproval.Spec), spec => Assert.NotEmpty(spec.Slots));
        Assert.Empty(SessionApproval.Spec.Slots);
    }
}
