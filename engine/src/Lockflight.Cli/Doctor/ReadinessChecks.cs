using System.Text.Json;
using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Init;
using Lockflight.Cli.Linking;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Projects;

namespace Lockflight.Cli.Doctor;

/// <summary>The checks <c>lockflight doctor</c> runs against a project root.</summary>
internal static class ReadinessChecks
{
    private const string RestoreAndLink = "dotnet tool restore && dotnet lockflight link";
    private const string PresetId = "lockflight";

    /// <summary>
    /// The fix for a pin or link that does not match this engine: a user-level install (whose dev versions are on no feed) re-pins
    /// the project to itself with <c>init</c>; a tool pin restores and links.
    /// </summary>
    private static string Relink => EngineCommand.IsUserInstall
        ? $"{EngineCommand.UserInstall} init (re-pins the project to the installed engine and links it; then commit dotnet-tools.json). A person's step: the tool pin is protected"
        : RestoreAndLink;
    private const string PresetCheck = "lockflight preset";
    private const string AddPreset = "uvx --from specify-cli==1.1.3 specify preset add --dev <plugin>/speckit-preset (https://github.com/lockflight/lockflight/blob/main/docs/guides/getting-started.md)";
    private const string CoverageCheck = "coverage collector";
    private const int MaxListed = 5;
    private const string Bash = "bash";
    private const string Python = "python3";

    public static Task<IReadOnlyList<DoctorCheck>> RunAsync(string root, CancellationToken cancellationToken) => RunAsync(root, userHome: null, TimeProvider.System, cancellationToken);

    /// <param name="userHome">The user's home, for the user-scope plugin check of a workspace; null skips that check's input (reported as not enabled).</param>
    /// <param name="time">The clock, for the window of Stop timings read from the ledger.</param>
    public static async Task<IReadOnlyList<DoctorCheck>> RunAsync(string root, string? userHome, TimeProvider time, CancellationToken cancellationToken)
    {
        List<DoctorCheck> checks = [CheckManifest(root), CheckPin(root), await CheckLinkAsync(root, cancellationToken), CheckGitIgnore(root), CheckClaudeSettings(root)];
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
        return checks;
    }

    /// <summary>The standard profile's init writes the evidence rule into .gitattributes; the lite profile does not.</summary>
    private static bool IsStandardProfile(string root)
    {
        string path = Path.Combine(root, ".gitattributes");
        return File.Exists(path) && File.ReadAllLines(path).Any(line => line.Trim() == GitAttributesUpdater.Rule);
    }

    private static DoctorCheck CheckManifest(string root) => ManifestLocator.Discover(root) switch
    {
        { Status: ManifestLoadStatus.Loaded } => new("manifest", CheckStatus.Pass, Manifest.FileName + " is valid", null),
        { Error: ManifestError error } => new("manifest", CheckStatus.Fail, error.Describe(), $"fix {Manifest.FileName} (a human edit; it is protected)"),
        _ => new("manifest", CheckStatus.Fail, $"no {Manifest.FileName}", $"{EngineCommand.Name} init"),
    };

    private static DoctorCheck CheckPin(string root) => ToolPin.ReadPinnedVersion(root) switch
    {
        null => new("tool pin", CheckStatus.Fail, "dotnet-tools.json does not pin lockflight.cli", $"{EngineCommand.Name} init"),
        string pinned when ToolPin.Matches(pinned, ToolPin.RunningVersion) => new("tool pin", CheckStatus.Pass, $"pinned {pinned}", null),
        string pinned => new("tool pin", CheckStatus.Fail, $"pinned {pinned}, running {ToolPin.RunningVersion}", Relink),
    };

    private static async Task<DoctorCheck> CheckLinkAsync(string root, CancellationToken cancellationToken)
    {
        string binary = Path.Combine(root, LinkCommand.RelativeBinDirectory, OperatingSystem.IsWindows() ? "lockflight.exe" : "lockflight");
        if (!File.Exists(binary))
        {
            return new("linked engine", CheckStatus.Fail, "hooks have no engine to run (recovery mode)", Relink);
        }

        ProcessResult versionRun = await ProcessRunner.RunAsync(binary, ["version"], root, cancellationToken);
        string linkedVersion = versionRun.Output.Trim();
        return ToolPin.Matches(linkedVersion, ToolPin.RunningVersion)
            ? new("linked engine", CheckStatus.Pass, $"{linkedVersion} at {LinkCommand.RelativeBinDirectory}", null)
            : new("linked engine", CheckStatus.Fail, $"linked {linkedVersion}, expected {ToolPin.RunningVersion}", Relink);
    }

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
        string path = Path.Combine(root, ".claude", "settings.json");
        JsonNode? settings = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : null;
        bool enabled = settings?["enabledPlugins"]?[Templates.PluginId]?.GetValue<bool>() == true;
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

        string registryPath = Path.Combine(root, ".specify", "presets", ".registry");
        JsonNode? preset;
        try
        {
            preset = File.Exists(registryPath) ? JsonNode.Parse(File.ReadAllText(registryPath))?["presets"]?[PresetId] : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return new(PresetCheck, missing, ".specify/presets/.registry is not readable JSON", AddPreset);
        }

        bool enabled = preset is JsonObject && preset["enabled"]?.GetValueKind() != JsonValueKind.False;
        return enabled
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
