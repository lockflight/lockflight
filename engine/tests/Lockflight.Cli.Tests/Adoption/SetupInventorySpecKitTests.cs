using Lockflight.Cli.Adoption;

namespace Lockflight.Cli.Tests.Adoption;

public sealed class SetupInventorySpecKitTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-inventory-");

    [Fact]
    public void AnOlderSpecKitIsUpgradedToThePinnedVersion()
    {
        SpecKit("1.1.0");

        AdoptionItem item = SpecKitItem();

        Assert.Equal(AdoptionAction.Upgrade, item.Action);
        Assert.Contains(SetupInventory.PinnedSpecKitVersion, item.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ANewerSpecKitIsKeptAndGetsOnlyThePreset()
    {
        SpecKit("1.2.3");
        Write(".claude/skills/speckit-plan/SKILL.md", "plan\n");

        AdoptionPlan plan = SetupInventory.Build(root.FullName, null, null, At);
        AdoptionItem item = Assert.Single(plan.Items, candidate => candidate.Area == AdoptionArea.SpecKit);
        AdoptionItem skill = Assert.Single(plan.Items, candidate => candidate.Subject == "speckit-plan");

        Assert.Equal(AdoptionAction.Add, item.Action);
        Assert.Contains("not downgraded", item.Reason, StringComparison.Ordinal);
        Assert.Contains("only the Lockflight preset", item.Reason, StringComparison.Ordinal);
        Assert.Equal(AdoptionAction.Keep, skill.Action);
        Assert.Contains("1.2.3", skill.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ANewerSpecKitWithThePresetIsKept()
    {
        SpecKit("1.2.3");
        Write(".specify/presets/.registry", """{ "presets": { "lockflight": {} } }""");

        Assert.Equal(AdoptionAction.Keep, SpecKitItem().Action);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private AdoptionItem SpecKitItem() =>
        Assert.Single(SetupInventory.Build(root.FullName, null, null, At).Items, item => item.Area == AdoptionArea.SpecKit);

    private void SpecKit(string version) => Write(".specify/init-options.json", $$"""{ "speckit_version": "{{version}}" }""");

    private void Write(string relative, string text)
    {
        string path = Path.Combine(root.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
