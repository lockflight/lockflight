using Lockflight.Cli.EngineLocks;

namespace Lockflight.Cli.Tests.EngineLocks;

public sealed class EngineHashesReaderTests
{
    /// <summary>The shape the release workflow writes (scripts/engine-hashes.py: indented, one property per line).</summary>
    private static string Released(string commit = "6675412e2fec3b1ed57b30de201d33278d61b7a6", params string[] rids)
    {
        string[] platforms = rids.Length > 0 ? rids : [.. EngineRids.Released];
        string binaries = string.Join(",\n", platforms.Select(rid => $$"""
                "{{rid}}": {
                  "sha256": "{{new string('a', 64)}}",
                  "path": "tools/any/{{rid}}/{{EngineRids.ExecutableName(rid)}}"
                }
            """));
        return $$"""
            {
              "schema": "lockflight.engine-hashes/v1",
              "package": "Lockflight.Cli",
              "version": "0.0.0-ci.10",
              "commit": "{{commit}}",
              "binaries": {
            {{binaries}}
              }
            }
            """;
    }

    [Fact]
    public void TheReleasedFileReads()
    {
        EngineHashes hashes = EngineHashesReader.Read(Released(), out string? problem)!;

        Assert.Null(problem);
        Assert.Equal(("0.0.0-ci.10", "tools/any/win-x64/lockflight.exe"), (hashes.Version, hashes.Binaries["win-x64"].Path));
        Assert.Equal(EngineRids.Released, hashes.Binaries.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AMissingPlatformIsAProblem()
    {
        Assert.Null(EngineHashesReader.Read(Released(rids: ["linux-x64", "osx-arm64"]), out string? problem));
        Assert.Equal("no well-formed hash and path for linux-arm64, osx-x64, win-x64", problem);
    }

    [Fact]
    public void AShortCommitIsAProblem()
    {
        Assert.Null(EngineHashesReader.Read(Released(commit: "6675412"), out string? problem));
        Assert.Equal("it needs a 'version' and a 40-character 'commit'", problem);
    }

    [Fact]
    public void AnotherSchemaIsAProblem()
    {
        Assert.Null(EngineHashesReader.Read(Released().Replace("engine-hashes/v1", "engine-hashes/v2", StringComparison.Ordinal), out string? problem));
        Assert.Equal("not a lockflight.engine-hashes/v1 file for Lockflight.Cli", problem);
    }

    [Fact]
    public void TheInstallScriptHashReadsWhenTheReleasePublishedIt()
    {
        string text = Released().Replace("\"binaries\": {", $"\"installScript\": {{ \"sha256\": \"{new string('5', 64)}\" }},\n  \"binaries\": {{", StringComparison.Ordinal);

        Assert.Equal(new string('5', 64), EngineHashesReader.Read(text, out _)!.InstallScriptSha256);
        Assert.Null(EngineHashesReader.Read(Released(), out _)!.InstallScriptSha256);
    }

    [Fact]
    public void AMalformedInstallScriptHashIsAProblem()
    {
        string text = Released().Replace("\"binaries\": {", "\"installScript\": { \"sha256\": \"abc\" },\n  \"binaries\": {", StringComparison.Ordinal);

        Assert.Null(EngineHashesReader.Read(text, out string? problem));
        Assert.Equal("'installScript' must be { \"sha256\": \"<64 lower-case hex>\" }", problem);
    }

    [Fact]
    public void ARepeatedKeyIsAProblem()
    {
        string text = Released().Replace("\"commit\":", "\"version\": \"9.9.9\",\n  \"commit\":", StringComparison.Ordinal);

        Assert.Null(EngineHashesReader.Read(text, out string? problem));
        Assert.Equal("the key 'version' appears twice", problem);
    }
}
