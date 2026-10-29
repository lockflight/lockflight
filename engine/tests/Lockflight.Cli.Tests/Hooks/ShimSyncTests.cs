using System.Text.RegularExpressions;
using Lockflight.Cli.Config;
using Lockflight.Cli.EngineLocks;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hooks.Adapters;
using Lockflight.Cli.Hooks.Requests;
using Lockflight.Cli.Linking;

namespace Lockflight.Cli.Tests.Hooks;

public sealed partial class ShimSyncTests
{
    private static readonly Lazy<string> Shim = new(() => File.ReadAllText(ShimLocation.Path));

    /// <summary>
    /// The shim keeps Claude Code's tool names for its own recovery decisions; the engine decides on kinds. The names the shim
    /// allows must be exactly the Claude Code tools the adapter maps to a kind that only reads.
    /// </summary>
    [Fact]
    public void TheShimAllowsTheClaudeCodeToolsTheAdapterMapsToReadOnlyKinds()
    {
        Match tools = ReadOnlyToolsCase().Match(Shim.Value);
        string[] readOnly = [.. ClaudeCodeAdapter.ToolKinds
            .Where(tool => new HookTool(tool.Key, tool.Value.Kind, tool.Value.Shell, WritesFiles: false).OnlyReads)
            .Select(tool => tool.Key)
            .Order(StringComparer.Ordinal)];

        Assert.True(tools.Success, "the shim's read-only tool case was not found");
        Assert.Equal(readOnly, tools.Groups["tools"].Value.Split('|').Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheShimRunsRecoveryCommandsInTheToolTheAdapterMapsToThePosixShell()
    {
        Match tool = RecoveryToolCheck().Match(Shim.Value);

        Assert.True(tool.Success, "the shim's recovery tool check was not found");
        Assert.Equal((ToolKind.Shell, ShellDialect.Posix), ClaudeCodeAdapter.KindOf(tool.Groups["tool"].Value));
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
    public void TheShimReadsTheEngineLocksTheEngineReads()
    {
        Assert.Contains($"lockfile=\"$root/{EngineLock.LocalRelativePath}\"", Shim.Value, StringComparison.Ordinal);
        Assert.Contains($"lockfile=\"$root/{EngineLock.RelativePath}\"", Shim.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInstallScriptReadsJsonWithTheShimsReader()
    {
        string script = File.ReadAllText(Path.Combine(Path.GetDirectoryName(ShimLocation.Path)!, "..", "..", "..", "scripts", "install.sh"));

        Assert.Equal(JsonStringFunction().Match(Shim.Value).Value, JsonStringFunction().Match(script).Value);
        Assert.True(JsonStringFunction().Match(script).Success, "install.sh has no json_string function");
    }

    [Fact]
    public void TheShimTakesThePlatformFromTheRecordAndReadsItsHashFromTheLock() =>
        Assert.Contains("json_string \"binaries.$recorded_rid.sha256\"", Shim.Value, StringComparison.Ordinal);

    [Fact]
    public void TheShimChecksTheRecordLinkWrites() =>
        Assert.Contains($"record=\"$root/{LinkCommand.RelativeBinDirectory}/{EngineRecord.FileName}\"", Shim.Value, StringComparison.Ordinal);

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

    [GeneratedRegex(@"(?ms)^json_string\(\) \{.*?^\}$")]
    private static partial Regex JsonStringFunction();

    [GeneratedRegex(@"case ""\$tool"" in\s+(?<tools>[A-Za-z|]+)\)")]
    private static partial Regex ReadOnlyToolsCase();

    [GeneratedRegex(@"\[ ""\$tool"" = ""(?<tool>[A-Za-z]+)"" \]")]
    private static partial Regex RecoveryToolCheck();

    [GeneratedRegex(@"case ""\$command"" in\s+(?<commands>""[^)]*"")\)")]
    private static partial Regex RecoveryCommandsCase();

    [GeneratedRegex(@"""(?<word>[^""]*)""")]
    private static partial Regex QuotedWord();

    [GeneratedRegex(@"grep -(?<flags>[A-Za-z]+) -e '(?<pattern>[^']*)'")]
    private static partial Regex DevelopmentGrep();

    [GeneratedRegex(@"case ""\$refusal"" in \*""(?<text>[^""]+)""\*\)")]
    private static partial Regex UnknownEventRefusalCase();
}
