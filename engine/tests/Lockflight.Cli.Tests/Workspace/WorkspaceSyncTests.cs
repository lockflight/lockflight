using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Init;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Workspace;

namespace Lockflight.Cli.Tests.Workspace;

public sealed class WorkspaceSyncTests : IDisposable
{
    private readonly DirectoryInfo shell = Directory.CreateTempSubdirectory("lockflight-sync-");

    private string Orders => Path.Combine(shell.FullName, "services", "orders");

    [Fact]
    public void TheUnitPolicyRebasesTheUnitsPathsAndKeepsTheAnywhereOnes()
    {
        Manifest manifest = new(shell.FullName, ["services/orders/src/Contracts/**", "**/appsettings.Local.json", "web/admin/dist/**", "secrets/**"], StopGate: false, StopTarget: null, GateSettings.Default);
        Unit orders = new("services/orders", "Orders.sln", UnitKind.Auto, "net8.0", StopFilter: null);

        string body = UnitPolicy.Body(manifest, orders);

        Assert.Contains("  - \"src/Contracts/**\"\n  - \"**/appsettings.Local.json\"\n", body, StringComparison.Ordinal);
        Assert.DoesNotContain("dist", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets", body, StringComparison.Ordinal);
        Assert.Contains("    framework: net8.0\n", body, StringComparison.Ordinal);
        Assert.Equal(body, UnitPolicy.BodyOf(UnitPolicy.Render(manifest, orders, "abc")));
    }

    [Theory]
    [InlineData("services/orders/src/Contracts/**", "src/Contracts/**")]
    [InlineData("services/*/Migrations/**", "Migrations/**")]
    [InlineData("*/*/appsettings.Production.json", "appsettings.Production.json")]
    [InlineData("services/**/db/**", "**/db/**")]
    [InlineData("**/appsettings.Local.json", "**/appsettings.Local.json")]
    [InlineData("services/orders", "**")]
    [InlineData("services/billing/**", null)]
    [InlineData("web/*/dist/**", null)]
    [InlineData("secrets/**", null)]
    public void ShellGlobsAreRebasedWhenTheyCanMatchInsideTheUnit(string glob, string? expected)
    {
        Assert.Equal(expected, UnitPolicy.Rebase(glob, "services/orders"));
    }

    [Fact]
    public void TheUnitsBuildFileTravelsWithItsPolicyAndLoads()
    {
        Manifest manifest = new(shell.FullName, [], StopGate: false, StopTarget: null, GateSettings.Default);
        string body = UnitPolicy.Body(manifest, new Unit("services/orders", "Orders.sln", UnitKind.Auto, TestFramework: null, StopFilter: null));

        ManifestLoadResult loaded = new ManifestParser(shell.FullName, "lockflight.unit.yaml").Parse(body);

        Assert.Equal("Orders.sln", loaded.Manifest!.Gates.BuildFile);
    }

    [Fact]
    public async Task SyncVendorsThePolicyCheckFindsDriftAndTheUnitLoadsItOnItsOwn()
    {
        await CreateWorkspaceAsync();

        (ExitCode sync, string output, _) = await TestConsole.RunAsync(["workspace", "sync"], string.Empty, shell.FullName);
        (ExitCode inSync, _, _) = await TestConsole.RunAsync(["workspace", "sync", "--check"], string.Empty, shell.FullName);
        File.AppendAllText(Path.Combine(shell.FullName, Manifest.FileName), "protected:\n  - \"services/orders/db/**\"\n");
        (ExitCode drifted, string driftOutput, _) = await TestConsole.RunAsync(["workspace", "sync", "--check"], string.Empty, shell.FullName);

        Assert.Equal(ExitCode.Pass, sync);
        Assert.Contains("synced   services/orders", output, StringComparison.Ordinal);
        Assert.DoesNotContain("web/admin", output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(Orders, Templates.WorkflowPath)));
        Assert.Contains("lockflight.unit.yaml", File.ReadAllText(Path.Combine(Orders, Templates.WorkflowPath)), StringComparison.Ordinal);
        Assert.Equal(ExitCode.Pass, inSync);
        File.AppendAllText(Path.Combine(Orders, Templates.WorkflowPath), "        continue-on-error: true\n");
        (ExitCode editedWorkflow, string editedOutput, _) = await TestConsole.RunAsync(["workspace", "sync", "--check"], string.Empty, shell.FullName);
        Assert.Equal(ExitCode.Fail, editedWorkflow);
        Assert.Contains("CI workflow differs from the generated one", editedOutput, StringComparison.Ordinal);
        await TestConsole.RunAsync(["workspace", "sync"], string.Empty, shell.FullName);
        Assert.Equal(ExitCode.Fail, drifted);
        Assert.Contains("DRIFT    services/orders (lockflight.unit.yaml)", driftOutput, StringComparison.Ordinal);

        // A clone of the unit alone (its CI) finds the vendored policy; inside the shell the shell's manifest wins.
        DirectoryInfo clone = Directory.CreateTempSubdirectory("lockflight-unit-ci-");
        try
        {
            File.Copy(Path.Combine(Orders, Manifest.UnitFileName), Path.Combine(clone.FullName, Manifest.UnitFileName));
            Assert.Equal(clone.FullName, ManifestLocator.Discover(clone.FullName).Manifest!.Root);
            Assert.Equal(shell.FullName, ManifestLocator.Discover(Orders).Manifest!.Root);
        }
        finally
        {
            TempDirectories.DeleteRecursively(clone);
        }
    }

    private async Task CreateWorkspaceAsync()
    {
        File.WriteAllText(Path.Combine(shell.FullName, Manifest.FileName), "version: 1\nunits:\n  - path: services/orders\n  - path: web/admin\n    kind: none\n");
        await GitAsync(shell.FullName, ["init", "-q"]);
        foreach (string unit in new[] { Orders, Path.Combine(shell.FullName, "web", "admin") })
        {
            Directory.CreateDirectory(unit);
            await GitAsync(unit, ["init", "-q"]);
        }

        File.WriteAllText(Path.Combine(Orders, "Orders.sln"), string.Empty);
    }

    private static async Task GitAsync(string directory, string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, directory, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Combined);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(shell);
}
