using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class HiddenTargetScenarioTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-hidden-");

    public HiddenTargetScenarioTests()
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "protected:\n  - secrets/**\n");
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "secrets"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, "secrets", "key.txt"), "key");
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "src"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, "src", "Directory.Build.props"), "<Project />");
    }

    [Theory]
    [InlineData("rm $(echo secrets/key.txt)")]
    [InlineData("rm `echo secrets/key.txt`")]
    [InlineData("rm $(ls secrets/*)")]
    [InlineData("tee $(echo secrets/key.txt) </dev/null")]
    [InlineData("rm $(cat list)")]
    [InlineData("echo x > $(echo secrets/key.txt)")]
    [InlineData("cp x $(echo secrets)")]
    [InlineData("rm \"$(echo secrets)/key.txt\"")]
    [InlineData("echo x > \"secrets/$(date).log\"")]
    [InlineData("cp x \"src/$(echo Directory.Build).props\"")]
    [InlineData("echo x > \"$(pwd)/lockflight.yaml\"")]
    [InlineData("echo x > \"$(git rev-parse --show-toplevel)/secrets/key.txt\"")]
    [InlineData("find . -name '*.tmp' | xargs rm")]
    [InlineData("echo x > \"$PWD/secrets/key.txt\"")]
    [InlineData("echo x > \"${HOME}/../../\"$X/secrets/key.txt")]
    [InlineData("echo x > \"$X/secrets/key.txt\"")]
    [InlineData("rm $DIR/lockflight.yaml")]
    [InlineData("echo x > secrets/{a,b}")]
    [InlineData("echo x > $'lockflight.yaml'")]
    [InlineData("busybox rm secrets/key.txt")]
    [InlineData("find . -name 'Directory.Build.props' -delete")]
    [InlineData("find . -name '*.props' -delete")]
    [InlineData("find . -name '*.txt' -delete")]
    [InlineData("find . -name '*.tmp' -exec rm {} +")]
    public async Task ComputedAndExpandedBashTargetsAreBlocked(string command)
    {
        (ExitCode exitCode, _, _) = await RunAsync("Bash", command);

        Assert.True(exitCode == ExitCode.HookBlock, $"'{command}' was allowed");
    }

    [Theory]
    [InlineData("Remove-Item (Join-Path secrets key.txt)")]
    [InlineData("Remove-Item $(Get-ChildItem secrets)")]
    [InlineData("Set-Content -Path (Join-Path secrets key.txt) -Value x")]
    [InlineData("Get-ChildItem secrets | Remove-Item")]
    [InlineData("Get-ChildItem *.tmp | Remove-Item")]
    [InlineData("'x' | Set-Content")]
    [InlineData("[IO.File]::WriteAllText('secrets/key.txt', 'x')")]
    [InlineData("[System.IO.File]::Delete('lockflight.yaml')")]
    [InlineData("$null = [IO.Directory]::Delete('secrets', $true)")]
    [InlineData("Get-Process | Export-Csv -Path lockflight.yaml")]
    [InlineData("cmd /c del secrets\\key.txt")]
    [InlineData("cmd /c \"type nul > lockflight.yaml\"")]
    [InlineData("Start-Process rm -ArgumentList secrets/key.txt")]
    public async Task ComputedPowerShellTargetsAreBlocked(string command)
    {
        (ExitCode exitCode, _, _) = await RunAsync("PowerShell", command);

        Assert.True(exitCode == ExitCode.HookBlock, $"'{command}' was allowed");
    }

    [Theory]
    [InlineData("Bash", "echo x > \"$TMPDIR/x.log\"")]
    [InlineData("Bash", "rm \"$FILE\"")]
    [InlineData("Bash", "echo \"$(date)\" > build.log")]
    [InlineData("Bash", "git checkout $(git rev-parse HEAD~1)")]
    [InlineData("Bash", "touch \"build-$(date +%s).log\"")]
    [InlineData("Bash", "echo x > \"logs/$(date +%F).log\"")]
    [InlineData("Bash", "mv build.log logs/$(date +%F).log")]
    [InlineData("Bash", "cp src/a.cs \"src/b-$(date +%s).cs\"")]
    [InlineData("Bash", "echo x > \"$(date +%s).log\"")]
    [InlineData("PowerShell", "'x' | Out-File \"logs\\$(Get-Date -f yyyy).log\"")]
    [InlineData("PowerShell", "Copy-Item src\\a.cs \"src\\b$(1).cs\"")]
    [InlineData("PowerShell", "New-Item -Name (Get-Date -f yyyy) -ItemType Directory")]
    [InlineData("PowerShell", "Set-Content src\\v.txt (Get-Date)")]
    [InlineData("PowerShell", "Add-Content log.txt \"$(Get-Date)\"")]
    [InlineData("Bash", "find . -type f -name '*.tmp' -delete")]
    [InlineData("Bash", "echo x > \"$PWD/notes.txt\"")]
    [InlineData("PowerShell", "Get-ChildItem | Select-Object Name")]
    [InlineData("PowerShell", "[IO.File]::ReadAllText('secrets/key.txt')")]
    [InlineData("PowerShell", "Get-Content a.txt | Set-Content b.txt")]
    public async Task OrdinaryComputedCommandsStayAllowed(string toolName, string command)
    {
        (ExitCode exitCode, _, string error) = await RunAsync(toolName, command);

        Assert.True(exitCode == ExitCode.Pass, $"'{command}' was blocked: {error}");
    }

    [Theory]
    [InlineData("Bash", "rm -rf ..")]
    [InlineData("Bash", "rm -rf ../..")]
    [InlineData("Bash", "rm -rf ~")]
    [InlineData("Bash", "rm -rf $HOME")]
    [InlineData("Bash", "cd src && rm -rf ../..")]
    [InlineData("Bash", "mv .. /tmp/elsewhere")]
    [InlineData("Bash", "rsync -a --delete /tmp/empty/ ..")]
    [InlineData("PowerShell", "Remove-Item -Recurse ..")]
    public async Task ATreeThatContainsTheProjectIsBlocked(string toolName, string command)
    {
        Dictionary<string, string> environment = new() { [EnvironmentVariables.Home] = Path.GetDirectoryName(projectRoot.FullName)! };

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["hook", "pre-tool-use"], Payload(toolName, command), projectRoot.FullName, environment);

        Assert.True(exitCode == ExitCode.HookBlock, $"'{command}' was allowed");
        Assert.Contains("contains this project", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnrelatedTreeOutsideTheProjectIsAllowed()
    {
        (ExitCode exitCode, _, string error) = await RunAsync("Bash", "rm -rf /tmp/lockflight-unrelated-x");

        Assert.True(exitCode == ExitCode.Pass, error);
    }

    [Fact]
    public async Task AnUnfollowableCdNamesTheSpecificRule()
    {
        (_, _, string protectedError) = await RunAsync("Bash", "cd $X && rm secrets/key.txt");
        OpenRedCycle();
        (_, _, string lockedError) = await RunAsync("Bash", "cd $X && rm ShippingTests.cs");

        Assert.Contains("'secrets/**'", protectedError, StringComparison.Ordinal);
        Assert.Contains("red cycle 1", lockedError, StringComparison.Ordinal);
    }

    private void OpenRedCycle()
    {
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "test", "Shop.Tests"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, "test", "Shop.Tests", "ShippingTests.cs"), "class ShippingTests;\n");
        RedGreenRecord record = new()
        {
            BaselineCommit = "abc",
            BaselineAt = DateTimeOffset.UnixEpoch,
            BaselineTests = new HashSet<string> { "T:A.B" },
            TestProjects = ["test/Shop.Tests"],
        };
        record.Cycles.Add(new RedGreenCycle(1, DateTimeOffset.UnixEpoch, GreenAt: null, [new RedGreenTest("T:A.C", "A.C", RedClass.RedStub, ["FR-001"])]));
        record.Save(Path.Combine(projectRoot.FullName, ".lockflight", "features", "f1", RedGreenRecord.FileName), "f1");
    }

    private Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(string toolName, string command) =>
        TestConsole.RunAsync(["hook", "pre-tool-use"], Payload(toolName, command), projectRoot.FullName);

    private string Payload(string toolName, string command) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["cwd"] = projectRoot.FullName,
        ["tool_name"] = toolName,
        ["tool_input"] = new Dictionary<string, string> { ["command"] = command },
    });

    public void Dispose() => projectRoot.Delete(recursive: true);
}
