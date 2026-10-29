using System.Text.Json;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class ShellGuardScenarioTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-shell-");

    private readonly DirectoryInfo scratchDirectory = Directory.CreateTempSubdirectory("lockflight-scratch-");

    // How an agent writes a path in Git Bash on Windows: forward slashes keep it literal (backslashes would be escapes).
    private static string ShellPath(string path) => path.Replace('\\', '/');

    public ShellGuardScenarioTests()
    {
        File.WriteAllText(Path.Combine(projectRoot.FullName, Manifest.FileName), "protected:\n  - secrets/**\n");
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "secrets"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, "secrets", "k"), "key");
        Directory.CreateDirectory(Path.Combine(projectRoot.FullName, "src", "Shop.Api"));
        File.WriteAllText(Path.Combine(projectRoot.FullName, "src", "Directory.Build.props"), "<Project />");
        File.WriteAllText(Path.Combine(projectRoot.FullName, "src", "Shop.Api", "Api.cs"), "class Api;");
    }

    [Theory]
    [InlineData("echo ok\nrm secrets/k")]
    [InlineData("echo ok\r\necho x > lockflight.yaml")]
    [InlineData("dotnet build 2>> secrets/k")]
    [InlineData("dotnet build &> src/Directory.Build.props")]
    [InlineData("cp /tmp/x secrets/")]
    [InlineData("cp /tmp/lockflight.yaml .")]
    [InlineData("cp -t src /tmp/Directory.Build.props")]
    [InlineData("cd src && cp /tmp/Directory.Build.props .")]
    [InlineData("cp -r /tmp/conf src")]
    [InlineData("sed -Ei 's/a/b/' src/Directory.Build.props")]
    [InlineData("sed --in-place=.bak 's/a/b/' lockflight.yaml")]
    [InlineData("perl -i -pe 's/a/b/' secrets/k")]
    [InlineData("rm -rf *")]
    [InlineData("rm secrets/*")]
    [InlineData("rm src/*.props")]
    [InlineData("echo x > lock*.yaml")]
    [InlineData("cd - && rm lockflight.yaml")]
    [InlineData("sudo rm secrets/k")]
    [InlineData("env FOO=1 rm lockflight.yaml")]
    [InlineData("if true; then rm secrets/k; fi")]
    [InlineData("echo $(rm secrets/k)")]
    [InlineData("bash -c 'echo x > lockflight.yaml'")]
    [InlineData("bash -ce \"rm lockflight.yaml\"")]
    [InlineData("bash -c -- \"rm lockflight.yaml\"")]
    [InlineData("git rm -r secrets")]
    [InlineData("git mv src/Directory.Build.props old.props")]
    [InlineData("git apply fix.patch")]
    [InlineData("git checkout .")]
    [InlineData("git checkout -f main")]
    [InlineData("git checkout src")]
    [InlineData("git apply --check .lockflight/local/proposals/20261015-101500-global-json.patch && git apply .lockflight/local/proposals/20261015-101500-global-json.patch")]
    public async Task CommonFormsOfAProtectedWriteAreBlocked(string command)
    {
        (ExitCode exitCode, _, string error) = await RunAsync("Bash", command);

        Assert.True(exitCode == ExitCode.HookBlock, $"'{command}' was allowed");
        Assert.Contains("lockflight:", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Set-Content -Path:lockflight.yaml -Value x")]
    [InlineData("Out-File -FilePath:secrets\\k")]
    [InlineData("Copy-Item C:\\tmp\\Directory.Build.props src")]
    [InlineData("Copy-Item -LiteralPath:C:\\tmp\\k -Destination:secrets")]
    [InlineData("Move-Item secrets old")]
    [InlineData("'x' | Tee-Object -FilePath lockflight.yaml")]
    [InlineData("Add-Content secrets\\k x")]
    [InlineData("& { Remove-Item secrets\\k }")]
    [InlineData("Write-Host ok\nRemove-Item lockflight.yaml")]
    public async Task PowerShellFormsOfAProtectedWriteAreBlocked(string command)
    {
        (ExitCode exitCode, _, _) = await RunAsync("PowerShell", command);

        Assert.True(exitCode == ExitCode.HookBlock, $"'{command}' was allowed");
    }

    [Theory]
    [InlineData("cd src && dotnet build")]
    [InlineData("cd src && echo x > a.txt")]
    [InlineData("cp /tmp/notes.txt src/")]
    [InlineData("cp /tmp/Api.cs src/Shop.Api/")]
    [InlineData("rm src/Shop.Api/*.cs")]
    [InlineData("sed -i 's/a/b/' src/Shop.Api/Api.cs")]
    [InlineData("dotnet build 2>&1 | tee build.log")]
    [InlineData("git checkout main")]
    [InlineData("git checkout -b feature/x")]
    [InlineData("git switch feature/x")]
    [InlineData("git pull && git merge main && git rebase main")]
    [InlineData("cat <<'EOF' > notes.md\nrm secrets/k\necho x > lockflight.yaml\nEOF")]
    [InlineData("echo '>' lockflight.yaml")]
    [InlineData("ls | xargs grep x")]
    [InlineData("find . -name '*.cs'")]
    [InlineData("tar -czf out.tgz src")]
    [InlineData("pushd src && echo x > a.txt && popd")]
    [InlineData("dotnet lockflight propose src/Directory.Build.props --from /tmp/props.new")]
    [InlineData("lockflight propose lockflight.yaml --from \"$TMPDIR/manifest.new\" secrets/k --delete")]
    [InlineData("git apply --check .lockflight/local/proposals/20261015-101500-global-json.patch")]
    [InlineData("bash -ec 'dotnet build && echo ok'")]
    [InlineData("sh -c 'touch \"$1\"' _ src/notes.txt")]
    [InlineData("bash <<'EOF'\ndotnet build > build.log\nEOF")]
    [InlineData("rm ${X:-build.log}")]
    [InlineData("ls src | while read f; do echo \"$f\"; done")]
    public async Task OrdinaryCommandsStayAllowed(string command)
    {
        (ExitCode exitCode, _, string error) = await RunAsync("Bash", command);

        Assert.True(exitCode == ExitCode.Pass, $"'{command}' was blocked: {error}");
    }

    [Theory]
    [InlineData("git checkout .")]
    [InlineData("git checkout test")]
    [InlineData("cd test/Shop.Tests && git checkout .")]
    [InlineData("git apply fix.patch")]
    [InlineData("sed -Ei 's/5m/6m/' test/Shop.Tests/ShippingTests.cs")]
    [InlineData("cp /tmp/ShippingTests.cs test/Shop.Tests/")]
    [InlineData("rm test/Shop.Tests/*.cs")]
    [InlineData("rm \"$PWD/test/Shop.Tests/ShippingTests.cs\"")]
    public async Task ShellRewritesOfLockedTestsAreBlockedDuringRed(string command)
    {
        OpenRedCycle();

        (ExitCode exitCode, _, string error) = await RunAsync("Bash", command);

        Assert.True(exitCode == ExitCode.HookBlock, $"'{command}' was allowed");
        Assert.Contains("lockflight:", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("S={scratch}; cd $S/async-uk && cat > dump.py <<'EOF'\nprint(1)\nEOF")]
    [InlineData("S={scratch}; git archive HEAD src/Directory.Build.props lockflight.yaml | tar -x -C $S")]
    [InlineData("export S={scratch} && echo x > ${S}/notes.txt")]
    public async Task WritesThroughAShellVariableAreNotJudged(string command)
    {
        string resolved = command.Replace("{scratch}", ShellPath(scratchDirectory.FullName), StringComparison.Ordinal);

        (ExitCode exitCode, _, string error) = await RunAsync("Bash", resolved);

        Assert.True(exitCode == ExitCode.Pass, $"'{resolved}' was blocked: {error}");
    }

    [Theory]
    [InlineData("bash -c \"lockflight approve spec\"")]
    [InlineData("eval 'dotnet lockflight redgreen reset'")]
    [InlineData("sh -c 'lockflight approve tasks'")]
    public async Task AReservedCommandInsideANestedCommandStringIsBlocked(string command)
    {
        (ExitCode exitCode, _, string error) = await RunAsync("Bash", command);

        Assert.True(exitCode == ExitCode.HookBlock, $"'{command}' was allowed");
        Assert.Contains("records a human decision", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASubshellTargetIsReportedWithoutItsParenthesis()
    {
        (_, _, string error) = await RunAsync("Bash", "(echo x > secrets/k)");

        Assert.Contains("'secrets/k'", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AProtectedPathBlockPointsTheAgentAtAProposal()
    {
        (_, _, string error) = await RunAsync("Bash", "echo x > src/Directory.Build.props");

        Assert.Contains("lockflight propose", error, StringComparison.Ordinal);
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

    private Task<(ExitCode ExitCode, string Output, string Error)> RunAsync(string toolName, string command)
    {
        string payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["cwd"] = projectRoot.FullName,
            ["tool_name"] = toolName,
            ["tool_input"] = new Dictionary<string, string> { ["command"] = command },
        });
        return TestConsole.RunAsync(["hook", "pre-tool-use"], payload, projectRoot.FullName);
    }

    public void Dispose()
    {
        projectRoot.Delete(recursive: true);
        scratchDirectory.Delete(recursive: true);
    }
}
