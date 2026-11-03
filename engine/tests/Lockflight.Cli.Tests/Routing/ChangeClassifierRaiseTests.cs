using Lockflight.Cli.Config;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Tests.Routing;

public sealed class ChangeClassifierRaiseTests : ChangeClassifierCases
{
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RaisesAndStricterPresetsNeverLowerTheClass(string name)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (ChangeSet changeSet, Manifest? manifest) = await ReadAsync(name, extraFile: null, cancellationToken);
        RoutingPolicy policy = Policy(manifest);
        Classification plain = ChangeClassifier.Classify(changeSet, policy, []);

        foreach (ChangeClass raise in Enum.GetValues<ChangeClass>())
        {
            Classification raised = ChangeClassifier.Classify(changeSet, policy, [new ClassRaise("label", raise)]);
            Assert.True(raised.Class >= plain.Class && raised.Class >= raise, $"a raise to {raise} gave {raised.Class}");
            Assert.Equal(plain.Computed, raised.Computed);
        }

        ChangeClass previous = ChangeClass.Fix;
        foreach (RoutingPreset preset in new[] { RoutingPreset.Relaxed, RoutingPreset.Default, RoutingPreset.Strict })
        {
            RoutingSettings stricter = RoutingPresets.For(preset) with
            {
                Critical = policy.Settings.Critical,
                Layers = policy.Settings.Layers,
                GeneratedPaths = policy.Settings.GeneratedPaths,
                NonProduction = policy.Settings.NonProduction,
            };
            ChangeClass atPreset = ChangeClassifier.Classify(changeSet, policy with { Settings = stricter }, []).Class;
            Assert.True(atPreset >= previous, $"{preset} gave {atPreset}, below the previous preset's {previous}");
            previous = atPreset;
        }
    }
}
