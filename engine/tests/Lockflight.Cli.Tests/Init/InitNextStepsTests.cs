using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Init;

public sealed class InitNextStepsTests
{
    [Fact]
    public void TheLiteProfileCommitsAndRunsDoctor() =>
        Assert.Equal(["Next: commit these files, then run `.lockflight/bin/lockflight doctor` from the project root."],
            InitNextSteps.For(InitProfile.Lite, SpecKitSetup.None, presetDirectory: null));

    [Fact]
    public void TheStandardProfileSetsUpSpecKitWithThePresetAndAcceptsFailingTests()
    {
        string text = string.Join('\n', InitNextSteps.For(InitProfile.Standard, SpecKitSetup.None, presetDirectory: "/home/me/lockflight/plugins/lockflight/speckit-preset"));

        Assert.Contains("specify init --here --force --integration claude --script sh", text, StringComparison.Ordinal);
        Assert.Contains("specify preset add --dev '/home/me/lockflight/plugins/lockflight/speckit-preset'", text, StringComparison.Ordinal);
        Assert.Contains("`.lockflight/bin/lockflight doctor`", text, StringComparison.Ordinal);
        Assert.Contains("`.lockflight/bin/lockflight baseline tests`", text, StringComparison.Ordinal);
        Assert.Contains("separate terminal window", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExistingSpecKitGetsThePresetThroughInitNotANewSpecKit()
    {
        string text = string.Join('\n', InitNextSteps.For(InitProfile.Standard, SpecKitSetup.WithoutPreset, presetDirectory: null));

        Assert.Contains("init --speckit-preset", text, StringComparison.Ordinal);
        Assert.DoesNotContain("specify init", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SpecKitWithThePresetAlreadyInstalledNeedsNoSpecKitStep()
    {
        IReadOnlyList<string> steps = InitNextSteps.For(InitProfile.Standard, SpecKitSetup.WithPreset, presetDirectory: null);
        string text = string.Join('\n', steps);

        Assert.DoesNotContain("adopt", text, StringComparison.Ordinal);
        Assert.DoesNotContain("specify init", text, StringComparison.Ordinal);
        Assert.DoesNotContain("preset add", text, StringComparison.Ordinal);
        Assert.StartsWith("  1. Commit these files, then run `.lockflight/bin/lockflight doctor`", steps[1], StringComparison.Ordinal);
        Assert.StartsWith("  2. Before the first feature, accept the tests that already fail", steps[2], StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownPresetDirectoryIsAPlaceholder() =>
        Assert.Contains("preset add --dev <plugin>/speckit-preset",
            string.Join('\n', InitNextSteps.For(InitProfile.Standard, SpecKitSetup.None, presetDirectory: null)), StringComparison.Ordinal);
}
