using Lockflight.Cli.Config;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Tests.Routing;

public sealed class UnitRoutingPolicyTests
{
    private static readonly RoutingSettings Shell = RoutingPresets.For(RoutingPreset.Default) with
    {
        Critical = ["services/orders/src/Payments/**", "services/billing/**", "**/Auth/**"],
        GeneratedPaths = ["services/orders/src/Clients/**", "services/orders/vendor/**"],
        Layers = [new RoutingLayer("domain", ["services/orders/src/*.Domain/**"]), new RoutingLayer("other", ["services/billing/**"])],
        FromManifest = true,
    };

    [Fact]
    public void TheShellsGlobsAreRebasedToTheUnitAndOtherUnitsGlobsDropped()
    {
        RoutingSettings merged = UnitRoutingPolicy.Merge(Shell, "services/orders", unit: null);

        Assert.Equal(["src/Payments/**", "**/Auth/**"], merged.Critical);
        Assert.Equal(["src/Clients/**", "vendor/**"], merged.GeneratedPaths);
        Assert.Equal(["domain"], merged.Layers.Select(layer => layer.Name));
        Assert.Equal(["src/*.Domain/**"], merged.Layers[0].Paths);
    }

    [Fact]
    public void TheUnitsOwnRoutingCanOnlyMakeItStricter()
    {
        RoutingSettings unit = RoutingPresets.For(RoutingPreset.Relaxed) with
        {
            Floor = ChangeClass.Behaviour,
            FixMaxLines = 5,
            Critical = ["src/Refunds/**"],
            GeneratedPaths = ["vendor/**", "src/**"],
            Coverage = new RoutingCoverage(90, 90, 90, 95),
            Require = new RoutingRequirements(ChangeClass.Critical, ChangeClass.Fix, ReviewKeyFrom: null, ChangeClass.Critical, ArchitectureDeclaredFrom: null),
            FromManifest = true,
        };

        RoutingSettings merged = UnitRoutingPolicy.Merge(Shell, "services/orders", unit);

        Assert.Equal(ChangeClass.Behaviour, merged.Floor);
        Assert.Equal(5, merged.FixMaxLines);
        Assert.Equal(2, merged.FixMaxFiles);
        Assert.Equal(400, merged.LargeLines);
        Assert.Equal(new RoutingCoverage(90, 90, 90, 95), merged.Coverage);
        Assert.Equal(ChangeClass.Feature, merged.Require.SpecFrom);
        Assert.Equal(ChangeClass.Fix, merged.Require.ReviewFrom);
        Assert.Equal(ChangeClass.Critical, merged.Require.ReviewKeyFrom);
        Assert.Equal(["src/Payments/**", "**/Auth/**", "src/Refunds/**"], merged.Critical);
        Assert.Equal(["vendor/**"], merged.GeneratedPaths);
    }
}
