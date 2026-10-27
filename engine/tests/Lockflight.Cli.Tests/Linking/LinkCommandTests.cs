using System.Security.Cryptography;
using System.Text;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Linking;

namespace Lockflight.Cli.Tests.Linking;

public sealed class LinkCommandTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-link-");

    private readonly DirectoryInfo install = Directory.CreateTempSubdirectory("lockflight-engine-");

    [Fact]
    public async Task ASingleFileEngineIsCopiedIntoTheProject()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");

        (ExitCode exitCode, string output, _) = await LinkAsync(new RunningEngine(EngineFile("native engine"), IsSingleFile: true));

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Equal("native engine", File.ReadAllText(LinkCommand.BinaryPath(root.FullName)));
        Assert.Contains("linked lockflight", output, StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(LinkCommand.BinaryPath(root.FullName)).HasFlag(UnixFileMode.UserExecute));
        }
    }

    [Fact]
    public async Task LinkingRecordsTheHashOfTheEngineItCopied()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");

        await LinkAsync(new RunningEngine(EngineFile("native engine"), IsSingleFile: true));

        string expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("native engine")));
        Assert.Equal(expected + "\n", File.ReadAllText(EngineRecord.PathOf(root.FullName)));
        Assert.Equal(EngineRecordStatus.Matches, EngineRecord.Check(root.FullName, LinkCommand.BinaryPath(root.FullName)));
    }

    [Fact]
    public async Task AnEngineReplacedAfterItWasLinkedNoLongerMatchesItsRecord()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        await LinkAsync(new RunningEngine(EngineFile("native engine"), IsSingleFile: true));

        File.WriteAllText(LinkCommand.BinaryPath(root.FullName), "another program");

        Assert.Equal(EngineRecordStatus.Differs, EngineRecord.Check(root.FullName, LinkCommand.BinaryPath(root.FullName)));
    }

    [Fact]
    public void AnEngineCopiedWithoutLinkHasNoRecord()
    {
        string binary = LinkCommand.BinaryPath(root.FullName);
        Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
        File.WriteAllText(binary, "copied by hand");

        Assert.Equal(EngineRecordStatus.NotRecorded, EngineRecord.Check(root.FullName, binary));
    }

    [Fact]
    public async Task AFrameworkDependentEngineIsRefusedAndNothingIsLinked()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");

        (ExitCode exitCode, _, string error) = await LinkAsync(new RunningEngine(EngineFile("launcher"), IsSingleFile: false));

        Assert.Equal(ExitCode.Error, exitCode);
        Assert.Contains("not a single-file native build", error, StringComparison.Ordinal);
        Assert.Contains("recovery mode", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(root.FullName, LinkCommand.RelativeBinDirectory)));
    }

    [Fact]
    public async Task ARefusedLinkKeepsTheEngineThatIsAlreadyLinked()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");
        await LinkAsync(new RunningEngine(EngineFile("native engine"), IsSingleFile: true));

        (ExitCode exitCode, _, _) = await LinkAsync(new RunningEngine(EngineFile("launcher"), IsSingleFile: false));

        Assert.Equal(ExitCode.Error, exitCode);
        Assert.Equal("native engine", File.ReadAllText(LinkCommand.BinaryPath(root.FullName)));
    }

    [Fact]
    public async Task AnEngineThatCannotBeLocatedIsAnError()
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), "version: 1\n");

        (ExitCode exitCode, _, string error) = await LinkAsync(new RunningEngine(null, IsSingleFile: true));

        Assert.Equal(ExitCode.Error, exitCode);
        Assert.Contains("cannot locate the running engine binary", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ThisTestProcessIsNotASingleFileEngine() => Assert.False(RunningEngine.Current.IsSingleFile);

    private string EngineFile(string content)
    {
        string path = Path.Combine(install.FullName, Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<(ExitCode ExitCode, string Output, string Error)> LinkAsync(RunningEngine engine)
    {
        using StringWriter output = new();
        using StringWriter error = new();
        CliContext context = new(new StringReader(string.Empty), output, error, root.FullName, TimeProvider.System)
        {
            Environment = EnvironmentReader.Of(new Dictionary<string, string>()),
            Engine = engine,
        };

        ExitCode exitCode = await CliApplication.RunAsync(["link"], context, TestContext.Current.CancellationToken);
        return (exitCode, output.ToString().Trim(), error.ToString().Trim());
    }

    public void Dispose()
    {
        TempDirectories.DeleteRecursively(root);
        TempDirectories.DeleteRecursively(install);
    }
}
