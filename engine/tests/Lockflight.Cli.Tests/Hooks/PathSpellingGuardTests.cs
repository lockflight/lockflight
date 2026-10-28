using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class PathSpellingGuardTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-spelling-");

    public PathSpellingGuardTests()
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "protected:\n  - secrets/**\n  - \"docs/caf\u00e9.md\"\n");
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, ".lockflight", "local"));
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "docs"));
    }

    [Theory]
    [InlineData("echo x >> l/../../lockflight.yaml")]
    [InlineData("echo e > l/../evidence.json")]
    [InlineData("cp /tmp/x l/../../secrets/k")]
    public async Task AParentSegmentAfterASymlinkIsJudgedWhereItReallyLeads(string command)
    {
        Assert.SkipUnless(SymbolicLinks.CanCreate, SymbolicLinks.SkipReason);
        Directory.CreateSymbolicLink(Path.Combine(projectRoot.FullName, "l"), Path.Combine(projectRoot.FullName, ".lockflight", "local"));

        (ExitCode exitCode, _, _) = await RunAsync("Bash", command);

        Assert.True(exitCode == ExitCode.HookBlock, $"'{command}' was allowed");
    }

    [Theory]
    [InlineData("Bash", "cp /tmp/x out/../secrets/k")]
    [InlineData("Write", "out/../secrets/k")]
    public async Task OnWindowsAParentSegmentAfterALinkElsewhereIsJudgedWhereWindowsCollapsesIt(string tool, string target)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && SymbolicLinks.CanCreate, "Windows only, with symbolic links.");
        DirectoryInfo elsewhere = Directory.CreateTempSubdirectory("lockflight-elsewhere-");
        try
        {
            Directory.CreateDirectory(Path.Combine(elsewhere.FullName, "inner"));
            Directory.CreateSymbolicLink(Path.Combine(projectRoot.FullName, "out"), Path.Combine(elsewhere.FullName, "inner"));

            (ExitCode exitCode, _, _) = await RunAsync(tool, target);

            Assert.True(exitCode == ExitCode.HookBlock, $"'{target}' was allowed");
        }
        finally
        {
            TempDirectories.DeleteRecursively(elsewhere);
        }
    }

    [Theory]
    [InlineData("echo x > docs/cafe\u0301.md")]
    [InlineData("rm docs/caf\u00e9.md")]
    public async Task EitherUnicodeSpellingOfAProtectedNameIsBlocked(string command)
    {
        (ExitCode exitCode, _, _) = await RunAsync("Bash", command);

        Assert.True(exitCode == ExitCode.HookBlock, $"'{command}' was allowed");
    }

    [Fact]
    public async Task AnEditOfTheDecomposedSpellingIsBlocked()
    {
        string payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["cwd"] = projectRoot.FullName,
            ["tool_name"] = "Write",
            ["tool_input"] = new Dictionary<string, string> { ["file_path"] = Path.Combine(projectRoot.FullName, "docs", "cafe\u0301.md"), ["content"] = "x" },
        });

        (ExitCode exitCode, _, _) = await TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);

        Assert.Equal(ExitCode.HookBlock, exitCode);
    }

    [Theory]
    [InlineData(@"\\?\{root}\lockflight.yaml")]
    [InlineData(@"\\.\{root}\secrets\k")]
    [InlineData(@"\\localhost\{drive}$\{rest}\lockflight.yaml")]
    [InlineData(@"\\?\UNC\localhost\{drive}$\{rest}\lockflight.yaml")]
    [InlineData(@"\\?\Volume{01234567-89ab-cdef-0123-456789abcdef}\x")]
    public async Task WindowsNamespaceAndSharePathsToAProtectedFileAreBlocked(string template)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows path namespaces.");
        string root = projectRoot.FullName.TrimEnd('\\');
        string target = template.Replace("{root}", root, StringComparison.Ordinal)
            .Replace("{drive}", root[..1], StringComparison.Ordinal)
            .Replace("{rest}", root[3..], StringComparison.Ordinal);

        (ExitCode exitCode, _, _) = await RunAsync("PowerShell", $"Remove-Item -LiteralPath '{target}'");

        Assert.True(exitCode == ExitCode.HookBlock, $"'{target}' was allowed");
    }

    [Theory]
    [InlineData("echo x > docs/notes.md")]
    [InlineData("echo x > l/notes.txt")]
    public async Task OrdinaryWritesStayAllowed(string command)
    {
        (ExitCode exitCode, _, string error) = await RunAsync("Bash", command);

        Assert.True(exitCode == ExitCode.Pass, $"'{command}' was blocked: {error}");
    }

    private Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(string toolName, string command)
    {
        string payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["cwd"] = projectRoot.FullName,
            ["tool_name"] = toolName,
            ["tool_input"] = toolName == "Write" ? new Dictionary<string, string> { ["file_path"] = command } : new Dictionary<string, string> { ["command"] = command },
        });
        return TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);
    }

    public void Dispose() => projectRoot.Delete(recursive: true);
}
