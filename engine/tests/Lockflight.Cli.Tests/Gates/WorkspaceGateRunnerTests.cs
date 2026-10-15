using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Gates;

public sealed class WorkspaceGateRunnerTests : IDisposable
{
    private readonly DirectoryInfo shell = Directory.CreateTempSubdirectory("lockflight-wsgate-");

    [Theory]
    [InlineData("Category!=Integration", new[] { "--filter-not-trait", "Category=Integration" })]
    [InlineData(" Speed = Fast ", new[] { "--filter-trait", "Speed=Fast" })]
    public void ASingleTraitConditionBecomesXunitsOwnOption(string filter, string[] expected)
    {
        Assert.Equal(expected, TestStep.TraitFilter(filter));
    }

    [Theory]
    [InlineData("Category!=Integration&Priority=1")]
    [InlineData("FullyQualifiedName~Orders")]
    public void AnythingMoreThanOneTraitConditionIsNotTranslated(string filter)
    {
        Assert.Null(TestStep.TraitFilter(filter));
    }

    [Fact]
    public async Task AWorkspaceWithoutChangesJudgesNothingAndRunsNoBuild()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Manifest manifest = await CreateWorkspaceAsync(cancellationToken);
        GateRunContext run = new(shell.FullName, shell.FullName, GateProfile.Stop, GateSettings.Default, BaseRef: null, TimeProvider.System);

        GateResult result = await WorkspaceGateRunner.RunAsync(manifest, run, [], useCache: false, TextWriter.Null, cancellationToken);

        Assert.Equal(Verdict.NotApplicable, result.Verdict);
        Assert.All(result.Steps, step => Assert.Equal("no changes, not run", step.Detail));
    }

    [Fact]
    public async Task AChangedUnitWithoutDotnetIsGuardedButNotBuilt()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Manifest manifest = await CreateWorkspaceAsync(cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(shell.FullName, "web", "admin", "app.ts"), "export const changed = 1;\n", cancellationToken);
        GateRunContext run = new(shell.FullName, shell.FullName, GateProfile.Stop, GateSettings.Default, BaseRef: null, TimeProvider.System);

        GateResult result = await WorkspaceGateRunner.RunAsync(manifest, run, ["web/admin"], useCache: false, TextWriter.Null, cancellationToken);

        GateStep admin = Assert.Single(result.Steps, step => step.Name == "web/admin");
        Assert.Equal("not a .NET unit: guarded, not built", admin.Detail);
    }

    [Fact]
    public async Task UnitStateSeesUncommittedAndNewFilesOfAChildRepository()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Manifest manifest = await CreateWorkspaceAsync(cancellationToken);
        Unit admin = manifest.Units[0];

        UnitState clean = await UnitState.ReadAsync(manifest, admin, "engine", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(shell.FullName, "web", "admin", "new.ts"), "export {};\n", cancellationToken);
        UnitState withNewFile = await UnitState.ReadAsync(manifest, admin, "engine", cancellationToken);

        Assert.False(clean.Dirty);
        Assert.True(withNewFile.Dirty);
        Assert.NotEqual(clean.Fingerprint, withNewFile.Fingerprint);
    }

    [Fact]
    public async Task AUnitOptionMustNameAUnit()
    {
        await CreateWorkspaceAsync(TestContext.Current.CancellationToken);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["gate", "run", "--unit", "services/missing"], string.Empty, shell.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("'services/missing' is not a unit", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AUnitOptionAppliesOnlyAtTheWorkspaceRoot()
    {
        await CreateWorkspaceAsync(TestContext.Current.CancellationToken);

        (ExitCode exitCode, _, string error) = await TestConsole.RunAsync(["gate", "run", "--unit", "web/admin", "--path", "web/admin"], string.Empty, shell.FullName);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Contains("--unit applies at the root", error, StringComparison.Ordinal);
    }

    private async Task<Manifest> CreateWorkspaceAsync(CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(shell.FullName, Manifest.FileName), "version: 1\nunits:\n  - path: web/admin\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(shell.FullName, ".gitignore"), "web/\n", cancellationToken);
        await GitAsync(shell.FullName, ["init", "-q"]);
        await GitAsync(shell.FullName, ["add", "."]);
        await GitAsync(shell.FullName, ["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "shell"]);
        string admin = Path.Combine(shell.FullName, "web", "admin");
        Directory.CreateDirectory(admin);
        await File.WriteAllTextAsync(Path.Combine(admin, "app.ts"), "export const app = 1;\n", cancellationToken);
        await GitAsync(admin, ["init", "-q"]);
        await GitAsync(admin, ["add", "."]);
        await GitAsync(admin, ["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "admin"]);
        return ManifestLocator.Discover(shell.FullName).Manifest!;
    }

    private static async Task GitAsync(string directory, string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, directory, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Combined);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(shell);
}
