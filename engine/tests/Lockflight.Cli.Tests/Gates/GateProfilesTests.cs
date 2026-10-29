using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Tests.Gates;

public sealed class GateProfilesTests : IDisposable
{
    /// <summary>How the help text names each step the pr profile adds to the stop profile's.</summary>
    private static readonly Dictionary<string, string> HelpWords = new(StringComparer.Ordinal)
    {
        ["policy.protected"] = "policy",
        ["coverage.patch"] = "patch coverage",
        ["security.deps"] = "dependencies",
        ["security.secrets"] = "secrets",
    };

    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-profiles-");

    [Fact]
    public void EveryProfileHasOneDefinitionWithDistinctStepNames()
    {
        Assert.Equal(Enum.GetValues<GateProfile>().Order(), GateProfiles.All.Select(definition => definition.Profile).Order());
        Assert.All(GateProfiles.All, definition =>
            Assert.Equal(definition.InOrder.Count, definition.InOrder.Select(step => step.Name).Distinct(StringComparer.Ordinal).Count()));
    }

    [Fact]
    public void RootStepsJudgeTheRootAndUnitStepsEachUnit() =>
        Assert.All(GateProfiles.All, definition =>
        {
            Assert.All(definition.RootFirst.Concat(definition.RootLast), step => Assert.Equal(StepScope.Root, step.Scope));
            Assert.All(definition.PerUnit, step => Assert.Equal(StepScope.Unit, step.Scope));
        });

    [Fact]
    public async Task TheHelpNamesEveryStepThePrProfileAddsToTheStopProfiles()
    {
        (_, string help, _) = await TestConsole.RunAsync(["--help"], string.Empty, directory.FullName);
        string[] lines = help.Split('\n');
        string prLine = lines[Array.FindIndex(lines, line => line.Trim().StartsWith("gate run --profile pr", StringComparison.Ordinal)) + 1];
        IEnumerable<string> stop = GateProfiles.For(GateProfile.Stop).InOrder.Select(step => step.Name);

        foreach (string added in GateProfiles.For(GateProfile.PullRequest).InOrder.Select(step => step.Name).Except(stop))
        {
            Assert.Contains(HelpWords[added], prLine, StringComparison.Ordinal);
        }
    }

    public void Dispose() => TempDirectories.DeleteRecursively(directory);
}
