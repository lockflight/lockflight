using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hosting;

public sealed class CommandOptionsTests : IDisposable
{
    private static readonly string[] Values = ["--feature", "--base"];
    private static readonly string[] Flags = ["--sign"];

    private readonly DirectoryInfo workingDirectory = Directory.CreateTempSubdirectory("lockflight-options-");

    [Fact]
    public void KnownOptionsAndFlagsAreRead()
    {
        CommandOptionsParse parse = CommandOptions.Parse(["--sign", "--feature", "f1"], Values, Flags);

        Assert.Equal("f1", parse.Options?.Value("--feature"));
        Assert.Null(parse.Options?.Value("--base"));
        Assert.True(parse.Options?.Flag("--sign"));
    }

    [Theory]
    [InlineData("unknown option '--feat'", "--feat", "f1")]
    [InlineData("unexpected argument 'f1'", "f1")]
    [InlineData("--feature needs a value", "--feature")]
    [InlineData("--feature needs a value (got '--sign'", "--feature", "--sign")]
    [InlineData("--base needs a value (got '-x'", "--base", "-x")]
    [InlineData("--feature is given more than once", "--feature", "a", "--feature", "b")]
    public void AnythingElseIsAUsageError(string expected, params string[] args)
    {
        CommandOptionsParse parse = CommandOptions.Parse(args, Values, Flags);

        Assert.Null(parse.Options);
        Assert.Contains(expected, parse.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gate", "run", "--profil", "pr")]
    [InlineData("gate", "run", "--profile")]
    [InlineData("gate", "run", "--base", "-x")]
    [InlineData("trace", "--feature")]
    [InlineData("review", "--modle", "opus")]
    [InlineData("phase", "check", "specify", "--featur", "f1")]
    [InlineData("redgreen", "status", "f1")]
    [InlineData("ledger", "retro", "--days", "soon")]
    [InlineData("ledger", "retro", "--days", "0")]
    public async Task ACommandWithAnOptionItDoesNotTakeExitsWithAConfigurationError(params string[] args)
    {
        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(args, "", workingDirectory.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.NotEmpty(error);
        Assert.DoesNotContain("Result:", output, StringComparison.Ordinal);
    }

    public void Dispose() => workingDirectory.Delete(recursive: true);
}
