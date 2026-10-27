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
    public async Task AUnitMissingFromTheCheckoutSaysSoInThePrProfile()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Manifest manifest = await CreateWorkspaceAsync(cancellationToken);
        Directory.Delete(Path.Combine(shell.FullName, "web", "admin"), recursive: true);
        GateRunContext run = new(shell.FullName, shell.FullName, GateProfile.PullRequest, GateSettings.Default, BaseRef: null, TimeProvider.System);

        GateResult result = await WorkspaceGateRunner.RunAsync(manifest, run, ["web/admin"], useCache: false, TextWriter.Null, cancellationToken);

        GateStep admin = Assert.Single(result.Steps, step => step.Name == "web/admin");
        Assert.Equal(Verdict.NotApplicable, admin.Verdict);
        Assert.Equal("not present in this checkout: not built", admin.Detail);
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
    public async Task ACommittedChangeInAUnitIsJudgedThoughItsWorkingTreeIsClean()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Manifest manifest = await CreateWorkspaceAsync(DotnetUnit, cancellationToken);
        GateRunContext run = new(shell.FullName, shell.FullName, GateProfile.Stop, GateSettings.Default, BaseRef: null, TimeProvider.System);
        await RememberPassesAsync(manifest, cancellationToken);
        GateResult unchanged = await WorkspaceGateRunner.RunAsync(manifest, run, [], useCache: true, TextWriter.Null, cancellationToken);

        string api = Path.Combine(shell.FullName, "services", "api");
        await File.WriteAllTextAsync(Path.Combine(api, "Api.cs"), "class Api { int broken }\n", cancellationToken);
        await GitAsync(api, ["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qam", "break"]);
        GateResult stop = await WorkspaceGateRunner.RunAsync(manifest, run, [], useCache: true, TextWriter.Null, cancellationToken);
        GateResult gateRun = await WorkspaceGateRunner.RunAsync(manifest, run, [], useCache: false, TextWriter.Null, cancellationToken);

        Assert.Equal("unchanged since its last PASS", Assert.Single(unchanged.Steps, step => step.Name == "services/api").Detail);
        Assert.Contains(stop.Steps, step => step.Name == "services/api: build");
        Assert.Contains(gateRun.Steps, step => step.Name == "services/api: build");
        Assert.NotEqual(Verdict.Pass, stop.Verdict);
    }

    [Fact]
    public async Task AUnitNeverJudgedIsJudgedThoughItHasNoChanges()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Manifest manifest = await CreateWorkspaceAsync(DotnetUnit, cancellationToken);
        GateRunContext run = new(shell.FullName, shell.FullName, GateProfile.Stop, GateSettings.Default, BaseRef: null, TimeProvider.System);

        GateResult result = await WorkspaceGateRunner.RunAsync(manifest, run, ["services/api"], useCache: true, TextWriter.Null, cancellationToken);

        Assert.Contains(result.Steps, step => step.Name == "services/api: build");
    }

    [Fact]
    public async Task ACommittedChangeToSharedBuildInputsInvalidatesEveryUnitsPass()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Manifest manifest = await CreateWorkspaceAsync(DotnetUnit, cancellationToken);
        GateRunContext run = new(shell.FullName, shell.FullName, GateProfile.Stop, GateSettings.Default, BaseRef: null, TimeProvider.System);
        await RememberPassesAsync(manifest, cancellationToken);

        await File.WriteAllTextAsync(Path.Combine(shell.FullName, "Directory.Build.props"), "<Project />\n", cancellationToken);
        await GitAsync(shell.FullName, ["add", "."]);
        await GitAsync(shell.FullName, ["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "shared props"]);
        GateResult result = await WorkspaceGateRunner.RunAsync(manifest, run, [], useCache: true, TextWriter.Null, cancellationToken);

        Assert.Contains(result.Steps, step => step.Name == "services/api: build");
    }

    [Fact]
    public async Task AMonorepoUnitsFingerprintIgnoresCommitsOutsideItAndFollowsCommitsInsideIt()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(Path.Combine(shell.FullName, Manifest.FileName), "version: 1\nunits:\n  - path: svc/a\n", cancellationToken);
        Directory.CreateDirectory(Path.Combine(shell.FullName, "svc", "a"));
        await File.WriteAllTextAsync(Path.Combine(shell.FullName, "svc", "a", "A.cs"), "class A {}\n", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(shell.FullName, "NOTES.md"), "notes\n", cancellationToken);
        await GitAsync(shell.FullName, ["init", "-q"]);
        await CommitAllAsync("base");
        Manifest manifest = ManifestLocator.Discover(shell.FullName).Manifest!;
        Unit unit = manifest.Units[0];

        UnitState before = await UnitState.ReadAsync(manifest, unit, "engine", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(shell.FullName, "NOTES.md"), "notes, edited\n", cancellationToken);
        await CommitAllAsync("notes");
        UnitState afterNotes = await UnitState.ReadAsync(manifest, unit, "engine", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(shell.FullName, "svc", "a", "A.cs"), "class A { int x; }\n", cancellationToken);
        await CommitAllAsync("unit");
        UnitState afterUnit = await UnitState.ReadAsync(manifest, unit, "engine", cancellationToken);

        Assert.Equal(before.Fingerprint, afterNotes.Fingerprint);
        Assert.NotEqual(before.Fingerprint, afterUnit.Fingerprint);
        Assert.False(afterUnit.Dirty);
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

    // A .NET unit with no project: its build fails at once, which is enough to see whether the unit was judged.
    private const string DotnetUnit = "  - path: services/api\n    kind: dotnet\n";

    private Task<Manifest> CreateWorkspaceAsync(CancellationToken cancellationToken) => CreateWorkspaceAsync(string.Empty, cancellationToken);

    private async Task<Manifest> CreateWorkspaceAsync(string extraUnits, CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(Path.Combine(shell.FullName, Manifest.FileName), $"version: 1\nunits:\n  - path: web/admin\n{extraUnits}", cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(shell.FullName, ".gitignore"), "web/\nservices/\n.lockflight/\n", cancellationToken);
        await GitAsync(shell.FullName, ["init", "-q"]);
        await CommitAllAsync("shell");
        await CreateChildAsync(Path.Combine(shell.FullName, "web", "admin"), "app.ts", "export const app = 1;\n", cancellationToken);
        if (extraUnits.Length > 0)
        {
            await CreateChildAsync(Path.Combine(shell.FullName, "services", "api"), "Api.cs", "class Api {}\n", cancellationToken);
        }

        return ManifestLocator.Discover(shell.FullName).Manifest!;
    }

    private static async Task CreateChildAsync(string directory, string file, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, file), content, cancellationToken);
        await GitAsync(directory, ["init", "-q"]);
        await GitAsync(directory, ["add", "."]);
        await GitAsync(directory, ["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "child"]);
    }

    private async Task CommitAllAsync(string message)
    {
        await GitAsync(shell.FullName, ["add", "."]);
        await GitAsync(shell.FullName, ["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", message]);
    }

    /// <summary>Records a PASS for every unit's current state, as a passing Stop gate would.</summary>
    private static async Task RememberPassesAsync(Manifest manifest, CancellationToken cancellationToken)
    {
        foreach ((Unit unit, UnitState state) in await WorkspaceGateRunner.ReadStatesAsync(manifest, [], cancellationToken))
        {
            await new StopGateCache(manifest.Root, unit.Path).RememberPassAsync(state.Fingerprint!, cancellationToken);
        }
    }

    private static async Task GitAsync(string directory, string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, directory, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Combined);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(shell);
}
