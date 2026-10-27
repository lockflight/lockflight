using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Init;

public sealed class InitNextStepsTests
{
    [Fact]
    public void TheLiteProfileCommitsAndRunsDoctor() =>
        Assert.Equal(["Next: commit these files, then run `.lockflight/bin/lockflight doctor` from the project root."],
            InitNextSteps.For(InitProfile.Lite, hasSpecKit: false, presetDirectory: null));

    [Fact]
    public void TheStandardProfileSetsUpSpecKitWithThePresetAndAcceptsFailingTests()
    {
        string text = string.Join('\n', InitNextSteps.For(InitProfile.Standard, hasSpecKit: false, presetDirectory: "/home/me/lockflight/plugins/lockflight/speckit-preset"));

        Assert.Contains("specify init --here --force --integration claude --script sh", text, StringComparison.Ordinal);
        Assert.Contains("specify preset add --dev '/home/me/lockflight/plugins/lockflight/speckit-preset'", text, StringComparison.Ordinal);
        Assert.Contains("`.lockflight/bin/lockflight doctor`", text, StringComparison.Ordinal);
        Assert.Contains("`.lockflight/bin/lockflight baseline tests`", text, StringComparison.Ordinal);
        Assert.Contains("separate terminal window", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExistingSpecKitIsAdoptedNotInitialisedAgain()
    {
        string text = string.Join('\n', InitNextSteps.For(InitProfile.Standard, hasSpecKit: true, presetDirectory: null));

        Assert.Contains("adopt plan", text, StringComparison.Ordinal);
        Assert.DoesNotContain("specify init", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownPresetDirectoryIsAPlaceholder() =>
        Assert.Contains("preset add --dev <plugin>/speckit-preset",
            string.Join('\n', InitNextSteps.For(InitProfile.Standard, hasSpecKit: false, presetDirectory: null)), StringComparison.Ordinal);
}
