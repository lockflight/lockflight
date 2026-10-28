using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Adoption;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Init;
using Lockflight.Cli.Linking;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Projects;
using Lockflight.Cli.Updating;

namespace Lockflight.Cli.Doctor;

/// <summary>The checks <c>lockflight doctor</c> runs against a project root.</summary>
internal static class ReadinessChecks
{
    private const string RestoreAndLink = "dotnet tool restore && dotnet lockflight link";

    private const string PresetCheck = "lockflight preset";
    private const string AddPreset = "uvx --from specify-cli==1.1.3 specify preset add --dev <plugin>/speckit-preset (https://github.com/lockflight/lockflight/blob/main/docs/guides/getting-started.md)";
    private const string CoverageCheck = "coverage collector";
    private const int MaxListed = 5;
    private const string Bash = "bash";
    private const string Python = "python3";
    private const string PinRestoreCheck = "pin restorable";
    private const string DevelopmentPinCheck = "development pin";
    private const string GitleaksCheck = "gitleaks";
    private const string ClaudeVersionCheck = "claude code";
    private const string Claude = "claude";

    /// <summary>The oldest Claude Code whose hooks honour <c>onFailure: block</c>: before it, a PreToolUse hook that times out does not block.</summary>
    private static readonly EngineVersion MinimumClaudeCode = new(2, 1, 295, string.Empty);

    private static readonly TimeSpan ClaudeVersionTimeLimit = TimeSpan.FromSeconds(30);

    public static Task<IReadOnlyList<DoctorCheck>> RunAsync(string root, CancellationToken cancellationToken) =>
        RunAsync(root, userHome: null, nugetPackages: null, gitleaks: null, TimeProvider.System, cancellationToken);

    /// <param name="userHome">
    /// The user's home, for the user-scope plugin check of a workspace and for the local feed and tools cache of the pin check; null
    /// skips those inputs (the plugin is reported as not enabled, the pin as not found locally).
    /// </param>
    /// <param name="nugetPackages">The NuGet global packages folder (<c>NUGET_PACKAGES</c>); null for its default under the home.</param>
    /// <param name="gitleaks">The gitleaks the secrets step runs (<c>LOCKFLIGHT_GITLEAKS</c>, else the one on PATH); null when there is none.</param>
    /// <param name="time">The clock, for the window of Stop timings read from the ledger.</param>
    public static async Task<IReadOnlyList<DoctorCheck>> RunAsync(
        string root, string? userHome, string? nugetPackages, string? gitleaks, TimeProvider time, CancellationToken cancellationToken)
    {
        List<DoctorCheck> checks = [CheckManifest(root), CheckPin(root)];
        if (CheckDevelopmentPin(root, userHome, nugetPackages) is DoctorCheck developmentPin)
        {
            checks.Add(developmentPin);
        }

        checks.Add(await CheckLinkAsync(root, cancellationToken));
        if (PluginEngineCheck.Check(root, userHome) is DoctorCheck pluginEngine)
        {
            checks.Add(pluginEngine);
        }

        checks.AddRange([CheckGitIgnore(root), CheckClaudeSettings(root)]);
        if (DenyRulesCheck.Check(root) is DoctorCheck denyRules)
        {
            checks.Add(denyRules);
        }

        if (SandboxCheck.Check(root, userHome) is DoctorCheck sandbox)
        {
            checks.Add(sandbox);
        }

        checks.AddRange([await CheckClaudeVersionAsync(root, Claude, cancellationToken), CheckGitleaks(gitleaks)]);
        if (OperatingSystem.IsWindows())
        {
            checks.Add(await CheckGitBashAsync(root, Bash, cancellationToken));
        }

        bool standardProfile = IsStandardProfile(root);
        // A lite project's own Spec Kit is the team's business: the preset and PyYAML matter only to Lockflight's lifecycle.
        if (standardProfile)
        {
            checks.Add(await CheckPyYamlAsync(root, Python, cancellationToken));
            checks.Add(CheckPreset(root, standardProfile));
            checks.Add(CheckClaudeCli());
        }

        if (ManifestLocator.Discover(root).Manifest?.Gates.Coverage.Collector is CoverageCollector collector && collector != CoverageCollector.None)
        {
            checks.Add(CheckCoverageExtension(root, collector));
        }

        checks.AddRange(await WorkspaceChecks.RunAsync(root, ManifestLocator.Discover(root).Manifest, userHome, time.GetUtcNow(), cancellationToken));
        checks.AddRange(await DocsChecks.RunAsync(root, cancellationToken));
        return checks;
    }

    /// <summary>
    /// The project uses the standard lifecycle: the standard profile's init wrote the evidence rule into .gitattributes (the lite
    /// profile does not), or the Lockflight Spec Kit preset is installed and enabled (as <c>adopt apply</c> does, after either init).
    /// </summary>
    private static bool IsStandardProfile(string root)
    {
        string path = Path.Combine(root, ".gitattributes");
        if (File.Exists(path) && File.ReadAllLines(path).Any(line => line.Trim() == GitAttributesUpdater.Rule))
        {
            return true;
        }

        return LockflightPresetRegistry.IsInstalled(root);
    }

    private static DoctorCheck CheckManifest(string root) => ManifestLocator.Discover(root) switch
    {
        { Status: ManifestLoadStatus.Loaded } => new("manifest", CheckStatus.Pass, Manifest.FileName + " is valid", null),
        { Error: ManifestError error } => new("manifest", CheckStatus.Fail, error.Describe(), $"fix {Manifest.FileName} (a human edit; it is protected)"),
        _ => new("manifest", CheckStatus.Fail, $"no {Manifest.FileName}", $"{EngineCommand.Name} init"),
    };

    private static DoctorCheck CheckPin(string root) => ToolPin.ReadPin(root) switch
    {
        { Status: ToolPinStatus.Unreadable, Problem: string problem } =>
            new("tool pin", CheckStatus.Fail, problem, $"fix the file by hand (a person's edit: it is protected), then {EngineCommand.Name} init"),
        { Version: string pinned } when ToolPin.Matches(pinned, ToolPin.RunningVersion) => new("tool pin", CheckStatus.Pass, $"pinned {pinned}", null),
        { Version: string pinned } => new("tool pin", CheckStatus.Fail, $"pinned {pinned}, running {ToolPin.RunningVersion}", Relink(pinned)),
        _ => new("tool pin", CheckStatus.Fail, "dotnet-tools.json does not pin lockflight.cli", $"{EngineCommand.Name} init"),
    };

    /// <summary>
    /// For a project pinned to a development build: when a release at or above the build's base is installed, the project should
    /// move to it (a development build is on no package feed, so a teammate or CI cannot restore it); otherwise whether the build
    /// can be restored on this machine (<see cref="CheckPinRestorable"/>). Null when the project pins no development build.
    /// </summary>
    public static DoctorCheck? CheckDevelopmentPin(string root, string? userHome, string? nugetPackages)
    {
        if (ToolPin.ReadPin(root) is { Status: ToolPinStatus.Pinned, Version: string pinned }
            && EngineVersion.Parse(pinned) is { IsDevelopment: true } development
            && userHome is not null && UserInstall.InstalledVersion(userHome) is string installed
            && EngineVersion.Parse(installed) is { IsDevelopment: false } release
            && release.CompareTo(development.DevelopmentBase()) >= 0)
        {
            return new(DevelopmentPinCheck, CheckStatus.Warn,
                $"the project pins the development build {pinned}, which no package feed has, and the release {installed} is installed",
                $"a person opens a separate terminal window ({HumanOnlyCommands.NotWithBang}) and runs `{EngineCommand.UserInstall} update` in the project: it re-pins the project to the newest release and links it; then commit dotnet-tools.json");
        }

        return CheckPinRestorable(root, userHome, nugetPackages);
    }

    /// <summary>
    /// Whether <c>dotnet tool restore</c> can get the pinned engine without the network: a development build is on no package feed,
    /// so it restores only from the NuGet tools cache, or from the local feed of an install from source when that feed is passed
    /// as a source. Null when the project pins no engine or a release version, which restores from a package feed (doctor never
    /// goes to the network to check).
    /// </summary>
    public static DoctorCheck? CheckPinRestorable(string root, string? userHome, string? nugetPackages)
    {
        if (ToolPin.ReadPin(root) is not { Status: ToolPinStatus.Pinned, Version: string pinned } || !EngineVersion.IsDevelopmentBuild(pinned))
        {
            return null;
        }

        string? packages = nugetPackages ?? (userHome is null ? null : Path.Combine(userHome, ".nuget", "packages"));
        if (packages is not null && Directory.Exists(Path.Combine(packages, "lockflight.cli", pinned.ToLowerInvariant())))
        {
            return new(PinRestoreCheck, CheckStatus.Pass, $"development build {pinned} is in this machine's tools cache", null);
        }

        string feed = userHome is null ? string.Empty : Path.Combine(UserInstall.Directory(userHome), "feed");
        bool inFeed = Directory.Exists(feed) && Directory.EnumerateFiles(feed, "*.nupkg")
            .Any(package => Path.GetFileName(package).Equals($"Lockflight.Cli.{pinned}.nupkg", StringComparison.OrdinalIgnoreCase));
        if (inFeed)
        {
            return new(PinRestoreCheck, CheckStatus.Warn,
                $"development build {pinned} is only in this machine's local feed (~/.lockflight/feed): a plain `dotnet tool restore` cannot get it, so `dotnet lockflight` does not run (the linked engine does)",
                "dotnet tool restore --add-source ~/.lockflight/feed, or run the engine as .lockflight/bin/lockflight");
        }

        bool installedFromSource = userHome is not null && File.Exists(UserInstall.Engine(userHome));
        return new(PinRestoreCheck, CheckStatus.Fail,
            $"development build {pinned} is in neither the local feed (~/.lockflight/feed) nor the tools cache: `dotnet tool restore` cannot get it, so `dotnet lockflight` and a fresh link fail",
            installedFromSource
                ? $"a person opens a separate terminal window ({HumanOnlyCommands.NotWithBang}) and runs `{EngineCommand.UserInstall} update` in the project: it re-pins the project to the installed engine and links it; then commit dotnet-tools.json"
                : $"install the engine (from a package, or from source with scripts/dev-install.sh in a Lockflight checkout), then a person opens a separate terminal window ({HumanOnlyCommands.NotWithBang}) and runs `{EngineCommand.UserInstall} update` in the project");
    }

    /// <summary>
    /// The fix for a pin or link that does not match this engine: a user-level install or a development pin (on no feed) re-pins the
    /// project to the installed engine with <c>init</c>; a release pin restores and links.
    /// </summary>
    private static string Relink(string? pinned) => EngineCommand.IsUserInstall || (pinned is not null && EngineVersion.IsDevelopmentBuild(pinned))
        ? $"{EngineCommand.UserInstall} init (re-pins the project to the installed engine and links it; then commit dotnet-tools.json). A person's step: the tool pin is protected"
        : RestoreAndLink;

    private static async Task<DoctorCheck> CheckLinkAsync(string root, CancellationToken cancellationToken)
    {
        string binary = LinkCommand.BinaryPath(root);
        string? pinned = ToolPin.ReadPin(root).Version;
        if (!File.Exists(binary))
        {
            return new("linked engine", CheckStatus.Fail, "hooks have no engine to run (recovery mode)", Relink(pinned));
        }

        if (UntrustedLink(root, binary) is DoctorCheck untrusted)
        {
            return untrusted;
        }

        ProcessResult versionRun = await ProcessRunner.RunAsync(binary, ["version"], root, cancellationToken);
        string linkedVersion = versionRun.Output.Trim();
        return ToolPin.Matches(linkedVersion, ToolPin.RunningVersion)
            ? new("linked engine", CheckStatus.Pass, $"{linkedVersion} at {LinkCommand.RelativeBinDirectory}, as linked", null)
            : new("linked engine", CheckStatus.Fail, $"linked {linkedVersion}, expected {ToolPin.RunningVersion}", Relink(pinned));
    }

    /// <summary>
    /// A linked engine the hooks do not run, so neither does doctor: one replaced since it was linked, or one without a hash record
    /// (linked by a Lockflight before 0.2.0-alpha.7, which wrote none, or copied in by hand). Null when it matches its record.
    /// An engine without a record may also be older than the plugin works with, and linking such an engine again writes no record,
    /// so its fix is <c>update</c>, which installs the newest engine, re-pins the project to it and links it.
    /// </summary>
    internal static DoctorCheck? UntrustedLink(string root, string binary) => EngineRecord.Check(root, binary) switch
    {
        EngineRecordStatus.Differs => new("linked engine", CheckStatus.Fail,
            $"replaced since it was linked: its SHA-256 is not the one link recorded in {LinkCommand.RelativeBinDirectory}/{EngineRecord.FileName}, so it was not run (the hooks do not run it either)",
            $"check what replaced it, then {Relink(ToolPin.ReadPin(root).Version)}"),
        EngineRecordStatus.NotRecorded => new("linked engine", CheckStatus.Fail,
            $"no hash recorded in {LinkCommand.RelativeBinDirectory}/{EngineRecord.FileName} (linked by a Lockflight older than 0.2.0-alpha.7, or copied in by hand), so it was not run (the hooks do not run it either)",
            $"a person opens a separate terminal window ({HumanOnlyCommands.NotWithBang}) and runs `{EngineCommand.UserInstall} update` in the project: it links the engine again and records its hash"),
        _ => null,
    };

    private static DoctorCheck CheckGitIgnore(string root)
    {
        string path = Path.Combine(root, ".gitignore");
        string content = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        List<string> missing = [.. Templates.GitIgnoreEntries.Where(entry => !content.Split('\n').Any(line => line.Trim() == entry))];
        return missing.Count == 0
            ? new("gitignore", CheckStatus.Pass, "local state is ignored", null)
            : new("gitignore", CheckStatus.Warn, "not ignored: " + string.Join(", ", missing), $"{EngineCommand.Name} init");
    }

    private static DoctorCheck CheckClaudeSettings(string root)
    {
        string path = Path.Combine(root, ClaudeSettingsMerger.RelativePath);
        string? text = File.Exists(path) ? File.ReadAllText(path) : null;
        if (ClaudeSettingsMerger.Problem(text) is string problem)
        {
            return new("claude settings", CheckStatus.Fail, $"{ClaudeSettingsMerger.RelativePath} {problem}", $"fix it by hand (Lockflight reads it as strict JSON: no comments or trailing commas), then {EngineCommand.Name} init");
        }

        JsonNode? settings = text is null ? null : JsonNode.Parse(text);
        bool enabled = settings?["enabledPlugins"]?[Templates.PluginId]?.GetValueKind() == JsonValueKind.True;
        bool marketplace = settings?["extraKnownMarketplaces"]?[Templates.MarketplaceName] is not null;
        return enabled && marketplace
            ? new("claude settings", CheckStatus.Pass, "plugin enabled for the project", null)
            : new("claude settings", CheckStatus.Warn, "teammates will not be offered the plugin", $"{EngineCommand.Name} init");
    }

    /// <summary>Whether <paramref name="bash"/> runs; a missing executable is a FAIL with the fix, never a crash.</summary>
    public static async Task<DoctorCheck> CheckGitBashAsync(string root, string bash, CancellationToken cancellationToken)
    {
        try
        {
            ProcessResult bashRun = await ProcessRunner.RunAsync(bash, ["--version"], root, cancellationToken);
            return bashRun.ExitCode == 0
                ? new("git bash", CheckStatus.Pass, "bash found for the plugin's hook shim", null)
                : new("git bash", CheckStatus.Fail, "bash did not run", "install Git for Windows (Git Bash is required)");
        }
        catch (Exception exception) when (exception is InvalidOperationException or ExecutableNotFoundException)
        {
            return new("git bash", CheckStatus.Fail, "bash is not on PATH", "install Git for Windows (Git Bash is required)");
        }
    }

    /// <summary>Whether the secrets step of the pr profile can run: without gitleaks it is NOT-EARNED, locally and in a CI without it.</summary>
    public static DoctorCheck CheckGitleaks(string? gitleaks) => gitleaks is not null
        ? new(GitleaksCheck, CheckStatus.Pass, $"the pr profile's secrets step can run ({gitleaks})", null)
        : new(GitleaksCheck, CheckStatus.Warn, "gitleaks is not on PATH, so the pr profile's secrets step is NOT-EARNED, not PASS",
            $"install gitleaks (https://github.com/gitleaks/gitleaks) on PATH, or set {EnvironmentVariables.Gitleaks} to its path");

    /// <summary>Runs <c>claude --version</c> and judges it with <see cref="CheckClaudeVersion"/>; a missing executable is a warning, never a crash.</summary>
    public static async Task<DoctorCheck> CheckClaudeVersionAsync(string root, string claude, CancellationToken cancellationToken)
    {
        try
        {
            ProcessResult versionRun = await ProcessRunner.RunAsync(claude, ["--version"], root, ClaudeVersionTimeLimit, cancellationToken);
            return CheckClaudeVersion(versionRun.ExitCode == 0 ? versionRun.Output : null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ExecutableNotFoundException)
        {
            return new(ClaudeVersionCheck, CheckStatus.Warn, $"claude is not on PATH, so its version is unknown; the hooks need Claude Code {MinimumClaudeCode} or newer", "install Claude Code, or check `claude --version` where you start it");
        }
    }

    /// <summary>
    /// Judges the output of <c>claude --version</c> (<c>2.1.309 (Claude Code)</c>): older than <see cref="MinimumClaudeCode"/>, or a
    /// version that cannot be read (<paramref name="versionOutput"/> null when the command failed), is a warning.
    /// </summary>
    public static DoctorCheck CheckClaudeVersion(string? versionOutput)
    {
        string? token = versionOutput?.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        EngineVersion? version = token is null ? null : EngineVersion.Parse(token);
        if (version is null)
        {
            return new(ClaudeVersionCheck, CheckStatus.Warn, $"the Claude Code version could not be read; the hooks need {MinimumClaudeCode} or newer", "run `claude --version`, and `claude update` when it is older");
        }

        return version.CompareTo(MinimumClaudeCode) >= 0
            ? new(ClaudeVersionCheck, CheckStatus.Pass, $"version {token}", null)
            : new(ClaudeVersionCheck, CheckStatus.Warn, $"version {token} is older than {MinimumClaudeCode}: a guard hook that times out does not block the tool call there", "claude update");
    }

    private static DoctorCheck CheckClaudeCli() => Executables.Find("claude", explicitPath: null) is string path
        ? new("claude cli", CheckStatus.Pass, $"review of record can run ({path})", null)
        : new("claude cli", CheckStatus.Warn, "lockflight review needs the claude CLI on PATH, signed in", "install Claude Code and sign in");

    /// <summary>
    /// <c>specify preset add</c> records installed presets in <c>.specify/presets/.registry</c> (JSON: <c>presets.&lt;id&gt;</c> with
    /// <c>enabled</c>). Without the Lockflight preset, Spec Kit writes tasks that <c>phase check tasks</c> rejects.
    /// </summary>
    private static DoctorCheck CheckPreset(string root, bool standardProfile)
    {
        CheckStatus missing = standardProfile ? CheckStatus.Fail : CheckStatus.Warn;
        if (!Directory.Exists(Path.Combine(root, ".specify")))
        {
            return new(PresetCheck, missing, "Spec Kit is not initialised (.specify/ is missing)", "uvx --from specify-cli==1.1.3 specify init --here --force --integration claude --script sh");
        }

        JsonNode? preset;
        try
        {
            preset = LockflightPresetRegistry.Entry(root);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return new(PresetCheck, missing, ".specify/presets/.registry is not readable JSON", AddPreset);
        }

        return LockflightPresetRegistry.IsEnabled(preset)
            ? new(PresetCheck, CheckStatus.Pass, $"installed (version {preset!["version"]?.ToString() ?? "unknown"})", null)
            : new(PresetCheck, missing, "the Lockflight Spec Kit preset is not installed or is disabled; tasks written from Spec Kit's stock template fail 'phase check tasks'", AddPreset);
    }

    /// <summary>
    /// Every test project (or a Directory.Build file above it) must reference the configured collector's package — the MTP extension
    /// or the VSTest data collector, by the platform <c>dotnet test</c> uses in that project's directory.
    /// </summary>
    private static DoctorCheck CheckCoverageExtension(string root, CoverageCollector collector)
    {
        IReadOnlyList<string> testProjects = ProjectFiles.TestProjects(root);
        if (testProjects.Count == 0)
        {
            return new(CoverageCheck, CheckStatus.Warn, $"gates.coverage.collector is set, but no test project was found that references {CoverageStep.PackageOf(collector, TestRunnerDetection.For(root))}", null);
        }

        (string Project, string Package)[] missing = [.. testProjects
            .Select(project => (Project: project, Package: CoverageStep.PackageOf(collector, TestRunnerDetection.For(Path.GetDirectoryName(project)!))!))
            .Where(entry => !ProjectFiles.References(root, entry.Project, entry.Package))
            .Select(entry => (Path.GetRelativePath(root, entry.Project).Replace('\\', '/'), entry.Package))];
        if (missing.Length == 0)
        {
            return new(CoverageCheck, CheckStatus.Pass, $"{testProjects.Count} test projects reference their coverage collector", null);
        }

        string packages = string.Join(" or ", missing.Select(entry => entry.Package).Distinct(StringComparer.Ordinal));
        return new(CoverageCheck, CheckStatus.Fail,
            $"gates.coverage.collector needs a collector package in every test project; missing in {string.Join(", ", missing.Take(MaxListed).Select(entry => $"{entry.Project} ({entry.Package})"))}",
            $"dotnet add <test project> package {packages}");
    }

    /// <summary>Whether <paramref name="python"/> can import PyYAML; a missing executable is a warning with the fix, never a crash.</summary>
    public static async Task<DoctorCheck> CheckPyYamlAsync(string root, string python, CancellationToken cancellationToken)
    {
        try
        {
            ProcessResult pythonRun = await ProcessRunner.RunAsync(python, ["-c", "import yaml"], root, cancellationToken);
            return pythonRun.ExitCode == 0
                ? new("spec kit presets", CheckStatus.Pass, "PyYAML available to Spec Kit's scripts", null)
                : new("spec kit presets", CheckStatus.Warn, "Spec Kit scripts need PyYAML once a preset is installed", "python3 -m pip install pyyaml; a Homebrew or other externally managed Python refuses that: python3 -m pip install --user --break-system-packages pyyaml");
        }
        catch (Exception exception) when (exception is InvalidOperationException or ExecutableNotFoundException)
        {
            return new("spec kit presets", CheckStatus.Warn, "python3 not found", "install Python 3 with PyYAML");
        }
    }
}
