using System.Runtime.CompilerServices;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Init;

/// <summary>
/// The CI files under <c>CiMigrationCases/</c> are what Lockflight 0.2's <c>init</c> wrote (pinned by version only). Migrated, each
/// must be exactly what this engine's <c>init</c> writes for the platform, and migrating again changes nothing.
/// </summary>
public sealed class CiStepMigrationTests
{
    /// <summary>The platforms whose current template still runs the gates in one job (GitHub's and Bitbucket's run the CI judge).</summary>
    public static TheoryData<string> Platforms() => ["azure", "gitlab"];

    /// <summary>The platforms whose current template runs the CI judge.</summary>
    public static TheoryData<string> JudgePlatforms() => ["github", "bitbucket"];

    [Theory]
    [MemberData(nameof(Platforms))]
    public void ALockflight02PipelineMovesToTheEngineLockAsInitWritesIt(string platform)
    {
        Assert.True(CiPlatformNames.TryParse(platform, out CiPlatform parsed));
        string expected = CiPipeline.Files(parsed, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), ["10.0.x"], CiTrigger.PullRequest, developmentPin: false)[0].Content;

        string? migrated = CiStepMigration.Migrate(File.ReadAllText(Path.Combine(CasesDirectory(), $"{platform}.yml")));

        Assert.Equal(expected, migrated);
        Assert.Equal(migrated, CiStepMigration.Migrate(migrated!));
    }

    [Theory]
    [MemberData(nameof(JudgePlatforms))]
    public void ALockflight02PipelineOfAJudgePlatformKeepsItsGatesJobAndMovesToTheEngineLock(string platform)
    {
        string? migrated = CiStepMigration.Migrate(File.ReadAllText(Path.Combine(CasesDirectory(), $"{platform}.yml")));

        Assert.NotNull(migrated);
        Assert.Contains(CiShellScripts.InstallBeginMarker, migrated, StringComparison.Ordinal);
        Assert.Contains($"for file in {CiShellScripts.PolicyFiles}; do", migrated, StringComparison.Ordinal);
        Assert.Equal(migrated, CiStepMigration.Migrate(migrated));
    }

    [Theory]
    [MemberData(nameof(JudgePlatforms))]
    public void TheCiJudgesPipelineIsCurrentAlready(string platform)
    {
        Assert.True(CiPlatformNames.TryParse(platform, out CiPlatform parsed));
        string judge = CiPipeline.Files(parsed, Path.GetTempPath(), ["10.0.x"], CiTrigger.PullRequest, developmentPin: false)[0].Content;

        Assert.Equal(judge, CiStepMigration.Migrate(judge));
    }

    [Fact]
    public void AHandEditedInstallStepIsLeftAlone()
    {
        string edited = File.ReadAllText(Path.Combine(CasesDirectory(), "github.yml")).Replace("--tool-path \"$RUNNER_TEMP/lockflight\"", "--tool-path /opt/lockflight", StringComparison.Ordinal);

        Assert.Null(CiStepMigration.Migrate(edited));
    }

    [Fact]
    public void AStepWithOnlyOneMarkerIsLeftAlone()
    {
        string current = CiPipeline.Files(CiPlatform.GitLab, Path.GetTempPath(), ["10.0.x"], CiTrigger.PullRequest, developmentPin: false)[0].Content;

        Assert.Null(CiStepMigration.Migrate(current.Replace(CiShellScripts.InstallEndMarker, "# removed", StringComparison.Ordinal)));
    }

    [Fact]
    public void CrlfLineEndingsAreKept()
    {
        string legacy = File.ReadAllText(Path.Combine(CasesDirectory(), "gitlab.yml")).ReplaceLineEndings("\r\n");

        string migrated = CiStepMigration.Migrate(legacy)!;

        Assert.DoesNotContain("\n", migrated.Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains(CiShellScripts.InstallBeginMarker, migrated, StringComparison.Ordinal);
    }

    private static string CasesDirectory([CallerFilePath] string path = "") => Path.Combine(Path.GetDirectoryName(path)!, "CiMigrationCases");
}
