using System.Text.RegularExpressions;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks;

namespace Lockflight.Cli.Tests.Hooks;

public sealed partial class ShimSyncTests
{
    private static readonly Lazy<string> Shim = new(() => File.ReadAllText(ShimPath()));

    [Fact]
    public void TheShimAllowsTheSameReadOnlyToolsAsTheEngine()
    {
        Match tools = ReadOnlyToolsCase().Match(Shim.Value);

        Assert.True(tools.Success, "the shim's read-only tool case was not found");
        Assert.Equal(ReadOnlyTools.Names.Order(StringComparer.Ordinal), tools.Groups["tools"].Value.Split('|').Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheShimAllowsTheSameRecoveryCommandsAsTheEngine()
    {
        Match commands = RecoveryCommandsCase().Match(Shim.Value);

        Assert.True(commands.Success, "the shim's recovery command case was not found");
        string[] shimCommands = [.. QuotedWord().Matches(commands.Groups["commands"].Value).Select(match => match.Groups["word"].Value)];
        Assert.Equal(RecoveryCommands.Commands.Order(StringComparer.Ordinal), shimCommands.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheShimRecognisesTheRefusalOfAnEngineThatDoesNotKnowAnEvent()
    {
        Match refusal = UnknownEventRefusalCase().Match(Shim.Value);
        DirectoryInfo project = Directory.CreateTempSubdirectory("lockflight-shim-event-");
        try
        {
            File.WriteAllText(Path.Combine(project.FullName, Manifest.FileName), "version: 1\n");

            (_, _, string error) = await TestConsole.RunAsync(["hook", "not-an-event"], "{}", project.FullName);

            Assert.True(refusal.Success, "the shim's unknown-event case was not found");
            Assert.Contains(refusal.Groups["text"].Value, error, StringComparison.Ordinal);
        }
        finally
        {
            project.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("0.2.0-alpha.4.dev.20261001")]
    [InlineData("0.2.1-0.dev.1")]
    [InlineData("0.2.0-dev.5")]
    [InlineData("0.2.0-DEV.5")]
    [InlineData("0.2.0-alpha.dev")]
    [InlineData("0.2.0-dev+abc")]
    [InlineData("0.2.0")]
    [InlineData("0.2.0-alpha.4")]
    [InlineData("0.2.0-devx.1")]
    [InlineData("0.2.0-alpha.develop.1")]
    [InlineData("1.0.0-rc.1+dev.3")]
    public void TheShimRecognisesTheSameDevelopmentBuildsAsTheEngine(string version)
    {
        Match grep = DevelopmentGrep().Match(Shim.Value);
        Assert.True(grep.Success, "the shim's development-build grep was not found");
        RegexOptions options = grep.Groups["flags"].Value.Contains('i', StringComparison.Ordinal) ? RegexOptions.IgnoreCase : RegexOptions.None;
        string pinLine = $"\"lockflight.cli\":{{\"version\":\"{version}\",\"commands\":[\"lockflight\"]}}";

        Assert.Equal(EngineVersion.IsDevelopmentBuild(version), Regex.IsMatch(pinLine, grep.Groups["pattern"].Value, options));
    }

    private static string ShimPath()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "plugins", "lockflight", "bin", "lockflight-hook");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("plugins/lockflight/bin/lockflight-hook was not found above the test binaries");
    }

    [GeneratedRegex(@"case ""\$tool"" in\s+(?<tools>[A-Za-z|]+)\)")]
    private static partial Regex ReadOnlyToolsCase();

    [GeneratedRegex(@"case ""\$command"" in\s+(?<commands>""[^)]*"")\)")]
    private static partial Regex RecoveryCommandsCase();

    [GeneratedRegex(@"""(?<word>[^""]*)""")]
    private static partial Regex QuotedWord();

    [GeneratedRegex(@"grep -(?<flags>[A-Za-z]+) -e '(?<pattern>[^']*)'")]
    private static partial Regex DevelopmentGrep();

    [GeneratedRegex(@"case ""\$refusal"" in \*""(?<text>[^""]+)""\*\)")]
    private static partial Regex UnknownEventRefusalCase();
}
