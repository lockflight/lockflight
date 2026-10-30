using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Init;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Workspace;
using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Tests.Workspace;

public sealed class ShellPointerTests : IDisposable
{
    private const string ShellManifest = """
        version: 2
        protected:
          - "services/orders/src/Contracts/**"
          - "web/**"
          - "**/appsettings.Production.json"
        gates:
          coverage:
            collector: coverlet
            patchThreshold: 90
            minChangedLines: 5
          quality:
            suppressions: fail
        units:
          - path: services/orders
            build: Orders.sln
            test:
              framework: net8.0
        """;

    private static readonly ShellSettings Pointer = new("https://example.com/shell.git", Ref: null, "services/orders");

    private readonly DirectoryInfo temp = Directory.CreateTempSubdirectory("lockflight-shell-");

    private string Shell => Path.Combine(temp.FullName, "shell");

    private string Orders => Path.Combine(Shell, "services", "orders");

    [Fact]
    public void TheShellsPolicyMakesTheUnitsOwnStricterAndNeverLooser()
    {
        Manifest unit = new(Orders, ["docs/**"], StopGate: false, StopTarget: null, GateSettings.Default with
        {
            Coverage = new CoverageSettings(CoverageCollector.None, 70, 20),
            Suppressions = GateMode.Off,
            DocsReferences = GateMode.Fail,
        });
        Manifest shellManifest = Parse(ShellManifest);

        Manifest effective = ShellPolicyOverlay.Apply(unit, new ShellPolicy(Shell, "abc", shellManifest, shellManifest.Units[0]));

        Assert.Equal(["docs/**", "src/Contracts/**", "**/appsettings.Production.json"], effective.ProtectedGlobs);
        Assert.Equal("Orders.sln", effective.Gates.BuildFile);
        Assert.Equal("net8.0", effective.Gates.TestFramework);
        Assert.Equal(new CoverageSettings(CoverageCollector.Coverlet, 90, 5), effective.Gates.Coverage);
        Assert.Equal(GateMode.Fail, effective.Gates.Suppressions);
        Assert.Equal(GateMode.Fail, effective.Gates.DocsReferences);
        Assert.Equal(Orders, effective.Root);
    }

    [Fact]
    public void TheShellsBuildFileAndFrameworkWinOverTheUnits()
    {
        Manifest unit = new(Orders, [], StopGate: false, StopTarget: null, GateSettings.Default with { TestFramework = "net10.0", BuildFile = "Own.slnx" });
        Manifest shellManifest = Parse(ShellManifest);

        Manifest effective = ShellPolicyOverlay.Apply(unit, new ShellPolicy(Shell, "abc", shellManifest, shellManifest.Units[0]));

        Assert.Equal("Orders.sln", effective.Gates.BuildFile);
        Assert.Equal("net8.0", effective.Gates.TestFramework);
    }

    [Fact]
    public void TheShellsRoutingReachesTheUnitRebasedAndOnlyStricter()
    {
        Manifest shellManifest = Parse(ShellManifest + "\nrouting:\n  preset: default\n  critical:\n    - \"services/orders/src/Payments/**\"\n    - \"services/billing/**\"\n");
        Manifest unit = new(Orders, [], StopGate: false, StopTarget: null, GateSettings.Default)
        {
            Routing = RoutingPresets.For(RoutingPreset.Relaxed) with { Critical = ["src/Refunds/**"], FromManifest = true },
        };

        Manifest effective = ShellPolicyOverlay.Apply(unit, new ShellPolicy(Shell, "abc", shellManifest, shellManifest.Units[0]));

        Assert.Equal(["src/Payments/**", "src/Refunds/**"], effective.Routing.Critical);
        Assert.Equal(20, effective.Routing.FixMaxLines);
        Assert.Equal(ChangeClass.Behaviour, effective.Routing.Require.ReviewFrom);
    }

    [Theory]
    [InlineData("fail", "off", "fail")]
    [InlineData("report", "auto", "auto")]
    [InlineData("off", "report", "report")]
    [InlineData("auto", "fail", "fail")]
    public void TheStricterGateModeWins(string first, string second, string expected)
    {
        Assert.Equal(Mode(expected), ShellPolicyOverlay.Stricter(Mode(first), Mode(second)));
        Assert.Equal(Mode(expected), ShellPolicyOverlay.Stricter(Mode(second), Mode(first)));
    }

    [Fact]
    public async Task TheShellsCommittedPolicyIsReadFromTheClone()
    {
        string commit = await CreateShellAsync();
        File.WriteAllText(Path.Combine(Shell, Manifest.FileName), "not: [valid");

        ShellPolicyReading reading = await ShellPolicyReader.ReadAsync(Shell, Pointer, checkOrigin: false, TestContext.Current.CancellationToken);

        Assert.Null(reading.Error);
        Assert.Equal(commit, reading.Policy!.Commit);
        Assert.Equal("Orders.sln", reading.Policy.Unit.Build);
    }

    [Fact]
    public async Task AShellThatDoesNotListTheUnitIsAnError()
    {
        await CreateShellAsync();

        ShellPolicyReading reading = await ShellPolicyReader.ReadAsync(Shell, Pointer with { Unit = "services/billing" }, checkOrigin: false, TestContext.Current.CancellationToken);

        Assert.Null(reading.Policy);
        Assert.Contains("lists no unit 'services/billing' (its units: services/orders)", reading.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADirectoryInsideAnotherRepositoryIsNoShellClone()
    {
        await CreateShellAsync();
        Directory.CreateDirectory(Path.Combine(Shell, "nested"));

        ShellPolicyReading reading = await ShellPolicyReader.ReadAsync(Path.Combine(Shell, "nested"), Pointer, checkOrigin: false, TestContext.Current.CancellationToken);

        Assert.Contains("is not a git clone of the workspace shell", reading.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AShellWithoutAManifestOrWithAnInvalidOneIsAnError()
    {
        Directory.CreateDirectory(Shell);
        await GitAsync(Shell, ["init", "-q"]);
        await GitAsync(Shell, ["-c", "user.name=t", "-c", "user.email=t@example.com", "commit", "-q", "--allow-empty", "-m", "empty"]);

        ShellPolicyReading missing = await ShellPolicyReader.ReadAsync(Shell, Pointer, checkOrigin: false, TestContext.Current.CancellationToken);
        File.WriteAllText(Path.Combine(Shell, Manifest.FileName), "version: 2\nunits: 3\n");
        await CommitAllAsync(Shell);
        ShellPolicyReading invalid = await ShellPolicyReader.ReadAsync(Shell, Pointer, checkOrigin: false, TestContext.Current.CancellationToken);

        Assert.Contains("has no lockflight.yaml", missing.Error, StringComparison.Ordinal);
        Assert.Contains("is invalid", invalid.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AUnitCheckedOutInsideItsShellFindsIt()
    {
        await CreateShellAsync();
        Directory.CreateDirectory(Orders);

        Assert.Equal(Shell, ShellLocator.FindEnclosing(Orders, Pointer));
        Assert.Null(ShellLocator.FindEnclosing(Orders, Pointer with { Unit = "services/billing" }));
        Assert.Null(ShellLocator.FindEnclosing(temp.FullName, Pointer));
    }

    [Fact]
    public async Task AUnitThatPointsAtTheShellIsNotVendoredAndItsManifestIsExpected()
    {
        await CreateShellAsync();
        Directory.CreateDirectory(Orders);
        await GitAsync(Orders, ["init", "-q"]);
        File.WriteAllText(Path.Combine(Orders, "Orders.sln"), string.Empty);
        Manifest shellManifest = new ManifestParser(Shell, Manifest.FileName).Parse(ShellManifest).Manifest!;
        Assert.Single(WorkspaceSyncCommand.Vendored(shellManifest));

        File.WriteAllText(Path.Combine(Orders, Manifest.FileName), "version: 2\nshell:\n  url: https://example.com/shell.git\n  unit: services/orders\n");

        Assert.Empty(WorkspaceSyncCommand.Vendored(shellManifest));
        Assert.True(UnitPointer.PointsToShell(Orders, shellManifest.Units[0]));
        Assert.False(UnitPointer.PointsToShell(Orders, shellManifest.Units[0] with { Path = "services/billing" }));
    }

    [Fact]
    public void TheTokenTravelsOnlyAsAnHttpsHeaderAndTracingIsOff()
    {
        EnvironmentReader environment = EnvironmentReader.Of(new Dictionary<string, string> { [ShellClone.TokenVariable] = "s3cret-token" });

        IReadOnlyDictionary<string, string?> https = ShellClone.GitEnvironment("https://example.com/shell.git", environment);
        IReadOnlyDictionary<string, string?> ssh = ShellClone.GitEnvironment("git@example.com:org/shell.git", environment);

        Assert.Equal("Authorization: Basic " + Convert.ToBase64String("x-access-token:s3cret-token"u8.ToArray()), https["GIT_CONFIG_VALUE_0"]);
        Assert.Equal("http.extraHeader", https["GIT_CONFIG_KEY_0"]);
        Assert.False(ssh.ContainsKey("GIT_CONFIG_VALUE_0"));
        Assert.Equal("https:ssh", https["GIT_ALLOW_PROTOCOL"]);
        Assert.Null(https["GIT_TRACE_CURL"]);
        Assert.Equal("1", https["GIT_TRACE_REDACT"]);
    }

    [Theory]
    [InlineData("https://github.com/org/shell.git", "git@github.com:org/shell.git", true)]
    [InlineData("ssh://git@github.com/org/shell", "https://github.com/org/shell.git/", true)]
    [InlineData("https://github.com/org/other.git", "https://github.com/org/shell.git", false)]
    [InlineData(null, "https://github.com/org/shell.git", false)]
    public void AHandGivenCloneMustComeFromTheShellThePointerNames(string? origin, string shellUrl, bool expected) =>
        Assert.Equal(expected, ShellPolicyReader.SameRepository(origin, shellUrl));

    [Fact]
    public async Task AFailedCloneSaysWhatAccessItNeedsWithoutTheToken()
    {
        EnvironmentReader environment = EnvironmentReader.Of(new Dictionary<string, string> { [ShellClone.TokenVariable] = "s3cret-token" });

        ShellCloneResult withToken = await ShellClone.CloneAsync("https://127.0.0.1:9/s3cret-token/shell.git", branch: null, Path.Combine(temp.FullName, "a"), environment, TestContext.Current.CancellationToken);
        ShellCloneResult azureMacro = await ShellClone.CloneAsync(
            "https://127.0.0.1:9/shell.git", branch: "main", Path.Combine(temp.FullName, "b"),
            EnvironmentReader.Of(new Dictionary<string, string> { [ShellClone.TokenVariable] = "$(LOCKFLIGHT_SHELL_TOKEN)" }), TestContext.Current.CancellationToken);

        Assert.Contains("check that LOCKFLIGHT_SHELL_TOKEN can read it", withToken.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret-token", withToken.Error, StringComparison.Ordinal);
        Assert.Contains("(branch main) failed", azureMacro.Error, StringComparison.Ordinal);
        Assert.Contains("a private shell needs a read-only token in LOCKFLIGHT_SHELL_TOKEN", azureMacro.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceCloneNeedsAUnitAndADirectoryOutsideTheRepository()
    {
        string repository = Path.Combine(temp.FullName, "orders");
        Directory.CreateDirectory(repository);
        File.WriteAllText(Path.Combine(repository, Manifest.FileName), "version: 2\n");

        (ExitCode notUnit, _, _) = await TestConsole.RunAsync(["workspace", "clone", "--into", Path.Combine(temp.FullName, "c")], string.Empty, repository);
        File.WriteAllText(Path.Combine(repository, Manifest.FileName), "version: 2\nshell:\n  url: https://example.com/shell.git\n  unit: services/orders\n");
        (ExitCode inside, _, string insideError) = await TestConsole.RunAsync(["workspace", "clone", "--into", "shell"], string.Empty, repository);
        (ExitCode missing, _, string missingError) = await TestConsole.RunAsync(["workspace", "clone"], string.Empty, repository);

        Assert.Equal(ExitCode.Pass, notUnit);
        Assert.Equal(ExitCode.Config, inside);
        Assert.Contains("is inside the repository", insideError, StringComparison.Ordinal);
        Assert.Equal(ExitCode.Config, missing);
        Assert.Contains("--into <dir> is required", missingError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APullRequestRunInAUnitCannotGoWithoutItsShell()
    {
        string repository = Path.Combine(temp.FullName, "orders");
        Directory.CreateDirectory(repository);
        File.WriteAllText(Path.Combine(repository, Manifest.FileName), "version: 2\nshell:\n  url: https://example.com/shell.git\n  unit: services/orders\n");

        (ExitCode noShell, _, string noShellError) = await TestConsole.RunAsync(["gate", "run", "--profile", "pr", "--base", "HEAD"], string.Empty, repository);
        (ExitCode badShell, _, string badShellError) = await TestConsole.RunAsync(["gate", "run", "--profile", "pr", "--shell", Path.Combine(temp.FullName, "none")], string.Empty, repository);

        Assert.Equal(ExitCode.Config, noShell);
        Assert.Contains("clone the shell with `lockflight workspace clone --into <dir>`", noShellError, StringComparison.Ordinal);
        Assert.Equal(ExitCode.Error, badShell);
        Assert.Contains("does not exist", badShellError, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("github", "--shell \"$RUNNER_TEMP/lockflight-shell\"", "${{ secrets.LOCKFLIGHT_SHELL_TOKEN }}")]
    [InlineData("azure", "--shell \"$AGENT_TEMPDIRECTORY/lockflight-shell\"", "LOCKFLIGHT_SHELL_TOKEN: $(LOCKFLIGHT_SHELL_TOKEN)")]
    [InlineData("bitbucket", "--shell \"$LOCKFLIGHT_TEMP/lockflight-shell\"", "unset LOCKFLIGHT_SHELL_TOKEN")]
    [InlineData("gitlab", "--shell \"$LOCKFLIGHT_TEMP/lockflight-shell\"", "unset LOCKFLIGHT_SHELL_TOKEN")]
    public void AUnitsCiClonesTheShellBeforeTheGatesAndJudgesWithIt(string platformName, string shellArgument, string token)
    {
        Assert.True(CiPlatformNames.TryParse(platformName, out CiPlatform platform));
        IReadOnlyList<CiFile> files = CiPipeline.Files(platform, temp.FullName, ["10.0"], CiTrigger.PullRequest, developmentPin: false);

        CiFile[] unitFiles = [.. files.Select(file => CiShellPointer.ForUnit(file, platform))];

        string job = unitFiles[0].Content;
        int clone = job.IndexOf("lockflight workspace clone --into", StringComparison.Ordinal);
        Assert.True(clone > 0 && clone < job.IndexOf(CiShellScripts.GateCommand, StringComparison.Ordinal));
        Assert.Contains(shellArgument, job, StringComparison.Ordinal);
        Assert.Contains(token, job, StringComparison.Ordinal);
        Assert.Equal(files.Skip(1), unitFiles.Skip(1));
        foreach (CiFile file in unitFiles)
        {
            new YamlStream().Load(new StringReader(file.Content));
        }
    }

    private async Task<string> CreateShellAsync()
    {
        Directory.CreateDirectory(Shell);
        File.WriteAllText(Path.Combine(Shell, Manifest.FileName), ShellManifest);
        await GitAsync(Shell, ["init", "-q"]);
        return await CommitAllAsync(Shell);
    }

    private static async Task<string> CommitAllAsync(string directory)
    {
        await GitAsync(directory, ["add", "-A"]);
        await GitAsync(directory, ["-c", "user.name=t", "-c", "user.email=t@example.com", "commit", "-q", "-m", "shell"]);
        return (await Git.CommitOfAsync(directory, "HEAD", TestContext.Current.CancellationToken))!;
    }

    private static GateMode Mode(string name) => name switch
    {
        "fail" => GateMode.Fail,
        "report" => GateMode.Report,
        "auto" => GateMode.Auto,
        _ => GateMode.Off,
    };

    private Manifest Parse(string text) => new ManifestParser(Shell, Manifest.FileName).Parse(text).Manifest!;

    private static async Task GitAsync(string directory, string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, directory, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Combined);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(temp);
}
