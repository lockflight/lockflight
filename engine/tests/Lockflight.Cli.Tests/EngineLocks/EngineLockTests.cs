using Lockflight.Cli.EngineLocks;

namespace Lockflight.Cli.Tests.EngineLocks;

public sealed class EngineLockTests
{
    private static readonly string ScriptHash = new('5', 64);

    private static EngineLock Release => new("0.3.0", EngineLockChannel.Release, ScriptHash,
        EngineRids.Released.ToDictionary(rid => rid, rid => Hash(rid), StringComparer.Ordinal));

    [Fact]
    public void TheLockIsWrittenInItsCanonicalForm() =>
        Assert.Equal($$"""
            {
              "schema": "lockflight.engine-lock/v1",
              "package": "Lockflight.Cli",
              "version": "0.3.0",
              "channel": "release",
              "installScript": { "sha256": "{{ScriptHash}}" },
              "binaries": {
                "linux-arm64": { "sha256": "{{Hash("linux-arm64")}}" },
                "linux-x64": { "sha256": "{{Hash("linux-x64")}}" },
                "osx-arm64": { "sha256": "{{Hash("osx-arm64")}}" },
                "osx-x64": { "sha256": "{{Hash("osx-x64")}}" },
                "win-x64": { "sha256": "{{Hash("win-x64")}}" }
              }
            }

            """.ReplaceLineEndings("\n"), EngineLockText.Write(Release));

    [Fact]
    public void AWrittenLockReadsBack()
    {
        EngineLock read = EngineLockReader.Read(EngineLockText.Write(Release), allowDevelopment: false, "0.3.0").Lock!;

        Assert.Equal((Release.Version, Release.Channel, Release.InstallScriptSha256), (read.Version, read.Channel, read.InstallScriptSha256));
        Assert.Equal(Release.Binaries.OrderBy(pair => pair.Key), read.Binaries.OrderBy(pair => pair.Key));
    }

    [Fact]
    public void AnUnknownPlatformIsIgnored()
    {
        string text = EngineLockText.Write(Release).Replace("\"win-x64\":", $"\"win-arm64\": {{ \"sha256\": \"{Hash("x")}\" }},\n    \"win-x64\":", StringComparison.Ordinal);

        EngineLockReading reading = EngineLockReader.Read(text, allowDevelopment: false, "0.3.0");

        Assert.Null(reading.Problem);
        Assert.Null(reading.Lock!.HashFor("win-arm64"));
    }

    [Theory]
    [InlineData("\"schema\": \"lockflight.engine-lock/v1\"", "\"schema\": \"lockflight.engine-lock/v2\"", "'schema' must be lockflight.engine-lock/v1")]
    [InlineData("\"package\": \"Lockflight.Cli\"", "\"package\": \"Other\"", "'package' must be Lockflight.Cli")]
    [InlineData("\"version\": \"0.3.0\"", "\"version\": \"0.3.0+abc\"", "'version' must be an engine version without build metadata, such as 0.3.0")]
    [InlineData("\"channel\": \"release\"", "\"channel\": \"nightly\"", "'channel' must be release or development")]
    [InlineData("\"channel\": \"release\"", "\"channel\": \"development\"", "a development lock belongs only in the git-ignored .lockflight/local/engine.lock; a committed lock is a release")]
    [InlineData("\"version\": \"0.3.0\",", "\"version\": \"0.3.0\", \"feed\": \"x\",", "unknown key 'feed' (known: schema, package, version, channel, installScript, binaries) — this engine is 0.3.0; a newer engine may know it")]
    [InlineData("{ \"sha256\": \"5555", "{ \"md5\": \"x\", \"sha256\": \"5555", "unknown key 'installScript.md5' (known: sha256)")]
    public void ALockOutsideItsContractIsAProblem(string from, string to, string problem) =>
        Assert.Equal(problem, EngineLockReader.Read(EngineLockText.Write(Release).Replace(from, to, StringComparison.Ordinal), allowDevelopment: false, "0.3.0").Problem);

    [Fact]
    public void AnUpperCaseHashIsAProblem() =>
        Assert.Equal(
            "'linux-x64' must be { \"sha256\": \"<64 lower-case hex>\" }",
            EngineLockReader.Read(EngineLockText.Write(Release).Replace(Hash("linux-x64"), Hash("linux-x64").ToUpperInvariant(), StringComparison.Ordinal), allowDevelopment: false, "0.3.0").Problem);

    [Fact]
    public void AReleaseLockNeedsEveryReleasedPlatform()
    {
        EngineLock partial = Release with { Binaries = new Dictionary<string, string> { ["osx-arm64"] = Hash("osx-arm64") } };

        Assert.Equal(
            "a release lock needs a hash for every released platform; missing: linux-arm64, linux-x64, osx-x64, win-x64",
            EngineLockReader.Read(EngineLockText.Write(partial), allowDevelopment: false, "0.3.0").Problem);
    }

    [Fact]
    public void ADevelopmentLockWithTheLocalPlatformIsReadWhereItIsAllowed()
    {
        EngineLock development = Release with
        {
            Version = "0.3.1-0.dev.20261101120000",
            Channel = EngineLockChannel.Development,
            Binaries = new Dictionary<string, string> { ["osx-arm64"] = Hash("osx-arm64") },
        };

        EngineLockReading reading = EngineLockReader.Read(EngineLockText.Write(development), allowDevelopment: true, "0.3.0");

        Assert.Equal((EngineLockChannel.Development, Hash("osx-arm64")), (reading.Lock!.Channel, reading.Lock.HashFor("osx-arm64")));
    }

    [Fact]
    public void AMissingLockFileIsNoLockAndNoProblem() =>
        Assert.Equal(new EngineLockReading(null, null), EngineLockReader.ReadFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), allowDevelopment: false, "0.3.0"));

    private static string Hash(string seed) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed)));
}
