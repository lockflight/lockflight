using Lockflight.Cli.Config;

namespace Lockflight.Cli.Tests.Config;

public sealed class ManifestVersionTwoTests
{
    private const string Engine = "0.3.0";

    private static ManifestLoadResult Parse(string yaml, string engine = Engine) =>
        new ManifestParser("/repo", "/repo/lockflight.yaml") { Engine = engine }.Parse(yaml);

    private static Manifest Load(string yaml)
    {
        ManifestLoadResult result = Parse(yaml);
        Assert.True(result.Status == ManifestLoadStatus.Loaded, result.Error?.Describe());
        return result.Manifest!;
    }

    private static string Error(string yaml, string engine = Engine)
    {
        ManifestLoadResult result = Parse(yaml, engine);
        Assert.Equal(ManifestLoadStatus.Invalid, result.Status);
        return result.Error!.Message;
    }

    [Fact]
    public void AVersionTwoManifestJudgesTheNewChecksByDefault()
    {
        Manifest manifest = Load("version: 2\n");

        Assert.Equal(2, manifest.Version);
        Assert.Equal(GateMode.Fail, manifest.Gates.Suppressions);
        Assert.Equal(ApprovalCheckMode.Fail, manifest.Approvals.Check);
        Assert.Equal(ApprovalMode.SamePr, manifest.Approvals.Mode);
        Assert.Equal(["specs/**"], manifest.Approvals.SpecPaths);
        Assert.Null(manifest.Autonomy);
        Assert.Null(manifest.Shell);
    }

    [Theory]
    [InlineData("version: 1\n")]
    [InlineData("protected: []\n")]
    public void AVersionOneManifestIsReadWithMigrationDefaultsThatOnlyReport(string yaml)
    {
        Manifest manifest = Load(yaml);

        Assert.Equal(1, manifest.Version);
        Assert.Equal(GateMode.Report, manifest.Gates.Suppressions);
        Assert.Equal(ApprovalCheckMode.Report, manifest.Approvals.Check);
        Assert.Equal(RoutingPreset.Default, manifest.Routing.Preset);
        Assert.False(manifest.Routing.FromManifest);
    }

    [Theory]
    [InlineData("routing:\n  preset: default\n", "routing")]
    [InlineData("approvals:\n  check: fail\n", "approvals")]
    [InlineData("autonomy:\n  maxClass: fix\n", "autonomy")]
    [InlineData("shell:\n  url: https://example.com/shell.git\n  unit: a\n", "shell")]
    [InlineData("requiresEngine: \"0.3.0\"\n", "requiresEngine")]
    public void AVersionOneManifestCannotHoldTheVersionTwoSections(string section, string key) =>
        Assert.Equal($"'{key}' needs 'version: 2' (this manifest is version 1)", Error("version: 1\n" + section));

    [Fact]
    public void AnUnknownVersionNamesTheEngineReadingIt() =>
        Assert.Equal("'version' must be 1 or 2 — this engine is 0.3.0; a newer engine may read a later version", Error("version: 3\n"));

    [Fact]
    public void AnUnknownKeyNamesTheEngineReadingIt() =>
        Assert.EndsWith("— this engine is 0.3.0; a newer engine may know it", Error("version: 2\ngates:\n  x: 1\n"));

    [Fact]
    public void ANewerRequiredEngineIsReportedBeforeAnUnknownKey() =>
        Assert.Equal(
            "this manifest needs Lockflight >= 0.4.0; this engine is 0.3.0 (ask the user to update the engine)",
            Error("version: 2\nnewSection: {}\nrequiresEngine: \"0.4.0\"\n"));

    [Theory]
    [InlineData("0.3.0")]
    [InlineData("0.2.0")]
    [InlineData("0.3.0-alpha.1")]
    public void ARequiredEngineAtOrBelowThisOneIsAccepted(string required) =>
        Assert.Equal(required, Load($"version: 2\nrequiresEngine: \"{required}\"\n").RequiresEngine);

    [Theory]
    [InlineData("0.0.0-local")]
    [InlineData("0.3.1-0.dev.20261101120000")]
    public void ADevelopmentBuildIsNotHeldToTheRequiredEngine(string engine) =>
        Assert.Equal(ManifestLoadStatus.Loaded, Parse("version: 2\nrequiresEngine: \"9.0.0\"\n", engine).Status);

    [Theory]
    [InlineData("three")]
    [InlineData("0.3")]
    [InlineData("0.3.0+abc")]
    public void ARequiredEngineThatIsNotAVersionIsAnError(string required) =>
        Assert.Equal("'requiresEngine' must be an engine version such as 0.3.0", Error($"version: 2\nrequiresEngine: \"{required}\"\n"));

    [Fact]
    public void AProtectedGlobListedTwiceIsAnErrorInVersionTwo() =>
        Assert.Equal("'protected' lists 'a/**' twice", Error("version: 2\nprotected: [\"a/**\", \"a/**\"]\n"));

    [Fact]
    public void AProtectedGlobListedTwiceStaysAllowedInVersionOne() =>
        Assert.Equal(["a/**", "a/**"], Load("version: 1\nprotected: [\"a/**\", \"a/**\"]\n").ProtectedGlobs);

    [Fact]
    public void WithoutARoutingSectionEveryValueIsTheDefaultPresetsAndNamedAsFilled()
    {
        RoutingSettings routing = Load("version: 2\n").Routing;

        Assert.Equal(RoutingPresets.For(RoutingPreset.Default) with { FilledFromPreset = RoutingPresets.Keys }, routing);
        Assert.False(routing.FromManifest);
    }

    [Fact]
    public void TheRoutingPresetSeedsTheKeysTheManifestLeavesOut()
    {
        RoutingSettings routing = Load("""
            version: 2
            routing:
              preset: strict
              fix:
                maxLines: 15
              coverage:
                critical: 99
              require:
                reviewKeyFrom: none
            """).Routing;

        Assert.Equal(RoutingPreset.Strict, routing.Preset);
        Assert.Equal(ChangeClass.Behaviour, routing.Floor);
        Assert.Equal(15, routing.FixMaxLines);
        Assert.Equal(1, routing.FixMaxFiles);
        Assert.Equal(new RoutingCoverage(85, 85, 90, 99), routing.Coverage);
        Assert.Null(routing.Require.ReviewKeyFrom);
        Assert.DoesNotContain("fix.maxLines", routing.FilledFromPreset);
        Assert.DoesNotContain("coverage.critical", routing.FilledFromPreset);
        Assert.DoesNotContain("require.reviewKeyFrom", routing.FilledFromPreset);
        Assert.Contains("fix.maxFiles", routing.FilledFromPreset);
        Assert.True(routing.FromManifest);
    }

    [Fact]
    public void EveryRoutingKeyIsRead()
    {
        RoutingSettings routing = Load("""
            version: 2
            routing:
              preset: default
              floor: behaviour
              fix: { maxLines: 30, maxFiles: 4 }
              largeLines: 500
              structural: feature
              critical: ["src/Payments/**", "src/*/Auth/**"]
              dbPaths: ["db/**"]
              generatedPaths: ["src/Generated/**"]
              nonProduction: ["samples/**"]
              requiredWorkflows: [".github/workflows/release.yml"]
              apiProjects: ["src/Shop.Api"]
              layers:
                - { name: domain, paths: ["src/*.Domain/**"] }
                - { name: infrastructure, paths: ["src/*.Infrastructure/**", "src/*.Api/**"] }
              coverage: { fix: 70, behaviour: 75, feature: 80, critical: 90 }
              require:
                specFrom: critical
                reviewFrom: fix
                reviewKeyFrom: behaviour
                architectureFrom: critical
                architectureDeclaredFrom: critical
            """).Routing;

        Assert.Equal(ChangeClass.Behaviour, routing.Floor);
        Assert.Equal((30, 4, 500, ChangeClass.Feature), (routing.FixMaxLines, routing.FixMaxFiles, routing.LargeLines, routing.Structural));
        Assert.Equal(["src/Payments/**", "src/*/Auth/**"], routing.Critical);
        Assert.Equal(["db/**"], routing.DbPaths);
        Assert.Equal(["src/Generated/**"], routing.GeneratedPaths);
        Assert.Equal(["samples/**"], routing.NonProduction);
        Assert.Equal([".github/workflows/release.yml"], routing.RequiredWorkflows);
        Assert.Equal(["src/Shop.Api"], routing.ApiProjects);
        Assert.Equal(["domain", "infrastructure"], routing.Layers.Select(layer => layer.Name));
        Assert.Equal(new RoutingCoverage(70, 75, 80, 90), routing.Coverage);
        Assert.Equal(
            new RoutingRequirements(ChangeClass.Critical, ChangeClass.Fix, ChangeClass.Behaviour, ChangeClass.Critical, ChangeClass.Critical),
            routing.Require);
        Assert.Empty(routing.FilledFromPreset);
    }

    [Theory]
    [InlineData("routing: {}\n", "'routing.preset' is required: relaxed, default or strict (the other keys override its values)")]
    [InlineData("routing:\n  preset: lite\n", "'routing.preset' must be relaxed, default or strict")]
    [InlineData("routing:\n  preset: default\n  structural: fix\n", "'routing.structural' must be feature or critical (a new project is never a fix)")]
    [InlineData("routing:\n  preset: default\n  floor: low\n", "'routing.floor' must be fix, behaviour, feature or critical")]
    [InlineData("routing:\n  preset: default\n  fix: { maxLines: 0 }\n", "'routing.fix.maxLines' must be a whole number of lines from 1 to 10000")]
    [InlineData("routing:\n  preset: default\n  fix: { maxFiles: 1001 }\n", "'routing.fix.maxFiles' must be a whole number of files from 1 to 1000")]
    [InlineData("routing:\n  preset: default\n  largeLines: 20\n", "'routing.largeLines' (20) must be above 'routing.fix.maxLines' (20)")]
    [InlineData("routing:\n  preset: default\n  coverage: { feature: 70 }\n",
        "'routing.coverage.feature' (70) is below 'routing.coverage.behaviour' (80); coverage never decreases from fix to critical")]
    [InlineData("routing:\n  preset: default\n  coverage: { fix: 101 }\n", "'routing.coverage.fix' must be a whole percentage from 0 to 100")]
    [InlineData("routing:\n  preset: default\n  require: { specFrom: fix }\n", "'routing.require.specFrom' must be behaviour, feature or critical")]
    [InlineData("routing:\n  preset: default\n  require: { architectureDeclaredFrom: feature }\n", "'routing.require.architectureDeclaredFrom' must be critical or none")]
    [InlineData("routing:\n  preset: default\n  require: { reviewFrom: feature, reviewKeyFrom: behaviour }\n",
        "'routing.require.reviewKeyFrom' (behaviour) is below 'routing.require.reviewFrom' (feature): a review key cannot be required where no review runs; raise it or set none")]
    [InlineData("routing:\n  preset: strict\n  require: { architectureFrom: critical, architectureDeclaredFrom: critical }\n  floor: fix\n", null)]
    [InlineData("routing:\n  preset: default\n  critical: [\"../shared/**\"]\n", "'routing.critical' entry '../shared/**' can never match: globs are relative to the project root, without '..'")]
    [InlineData("routing:\n  preset: default\n  critical: [\"/etc/**\"]\n", "'routing.critical' entry '/etc/**' can never match: globs are relative to the project root, without '..'")]
    [InlineData("routing:\n  preset: default\n  dbPaths: [\"db/**\", \"db/**\"]\n", "'routing.dbPaths' lists 'db/**' twice")]
    [InlineData("routing:\n  preset: default\n  layers: [{ name: domain, paths: [\"src/**\"] }]\n",
        "'routing.layers' needs at least 2 layers (a reference crosses from one to another)")]
    [InlineData("routing:\n  preset: default\n  layers: [{ name: a, paths: [\"x/**\"] }, { name: A, paths: [\"y/**\"] }]\n", "'routing.layers' names 'A' twice")]
    [InlineData("routing:\n  preset: default\n  layers: [{ name: a, paths: [\"src/**\"] }, { name: b, paths: [\"src/Api/**\"] }]\n",
        "'routing.layers' glob 'src/Api/**' of 'b' overlaps a glob of 'a'; a file belongs to one layer")]
    [InlineData("routing:\n  preset: default\n  layers: [{ name: a }, { name: b, paths: [\"y/**\"] }]\n", "every entry of 'routing.layers' needs a 'name' and 'paths'")]
    [InlineData("routing:\n  preset: default\n  require: { other: fix }\n",
        "unknown key 'routing.require.other' (known: specFrom, reviewFrom, reviewKeyFrom, architectureFrom, architectureDeclaredFrom) — this engine is 0.3.0; a newer engine may know it")]
    public void AnInvalidRoutingSectionIsAnErrorNamingTheRule(string routing, string? expected)
    {
        ManifestLoadResult result = Parse("version: 2\n" + routing);

        Assert.Equal(expected, result.Error?.Message);
    }

    [Fact]
    public void ARoutingErrorPointsAtTheKeyTheManifestWrote()
    {
        ManifestError error = Parse("version: 2\nrouting:\n  preset: default\n  fix:\n    maxLines: 500\n").Error!;

        Assert.Equal((5, 15), (error.Line, error.Column));
    }

    [Fact]
    public void EveryApprovalsKeyIsRead()
    {
        ApprovalSettings approvals = Load("""
            version: 2
            approvals:
              check: report
              default: spec-first
              specFirstFrom: feature
              specFirstPaths: ["src/Payments/**"]
              specPaths: ["specs/**", "docs/specs/**"]
            """).Approvals;

        Assert.Equal(ApprovalCheckMode.Report, approvals.Check);
        Assert.Equal(ApprovalMode.SpecFirst, approvals.Mode);
        Assert.Equal(ChangeClass.Feature, approvals.SpecFirstFrom);
        Assert.Equal(["src/Payments/**"], approvals.SpecFirstPaths);
        Assert.Equal(["specs/**", "docs/specs/**"], approvals.SpecPaths);
    }

    [Fact]
    public void TheStrictPresetSeedsSpecFirstFromCritical() =>
        Assert.Equal(ChangeClass.Critical, Load("version: 2\nrouting:\n  preset: strict\n").Approvals.SpecFirstFrom);

    [Fact]
    public void AnExplicitSpecFirstFromWinsOverThePreset() =>
        Assert.Null(Load("version: 2\nrouting:\n  preset: strict\napprovals:\n  specFirstFrom: none\n").Approvals.SpecFirstFrom);

    [Theory]
    [InlineData("check: off", "'approvals.check' must be fail or report")]
    [InlineData("default: later", "'approvals.default' must be same-pr or spec-first")]
    [InlineData("specFirstFrom: fix", "'approvals.specFirstFrom' must be none, feature or critical")]
    [InlineData("specPaths: []", "'approvals.specPaths' must list at least one glob (default [\"specs/**\"])")]
    public void AnInvalidApprovalsValueIsAnError(string line, string expected) =>
        Assert.Equal(expected, Error($"version: 2\napprovals:\n  {line}\n"));

    [Fact]
    public void TheReservedAutonomySectionIsValidatedAndKept() =>
        Assert.Equal(
            new AutonomySettings(ChangeClass.Fix, 5.00m, 60),
            Load("version: 2\nautonomy:\n  maxClass: fix\n  budgetUsd: 5.00\n  timeMinutes: 60\n").Autonomy);

    [Theory]
    [InlineData("maxClass: none", "'autonomy.maxClass' must be fix, behaviour, feature or critical")]
    [InlineData("budgetUsd: 0", "'autonomy.budgetUsd' must be an amount in US dollars above 0 and at most 100, such as 5.00")]
    [InlineData("budgetUsd: 100.01", "'autonomy.budgetUsd' must be an amount in US dollars above 0 and at most 100, such as 5.00")]
    [InlineData("timeMinutes: 1441", "'autonomy.timeMinutes' must be a whole number of minutes from 1 to 1440")]
    public void AnAutonomyValueOutOfRangeIsAnError(string line, string expected) =>
        Assert.Equal(expected, Error($"version: 2\nautonomy:\n  {line}\n"));

    [Theory]
    [InlineData("https://example.com/example/shell.git")]
    [InlineData("git@example.com:example/shell.git")]
    [InlineData("ssh://git@example.com/example/shell.git")]
    public void AShellUrlWithoutCredentialsIsAccepted(string url) =>
        Assert.Equal(new ShellSettings(url, "main", "services/orders"), Load($"version: 2\nshell:\n  url: {url}\n  ref: main\n  unit: services/orders/\n").Shell);

    [Theory]
    [InlineData("https://user:secret@example.com/example/shell.git")]
    [InlineData("https://ghp_token@example.com/example/shell.git")]
    [InlineData("https://example.com/example/shell.git?private_token=x")]
    [InlineData("ssh://git:secret@example.com/example/shell.git")]
    public void AShellUrlWithCredentialsIsRefused(string url) =>
        Assert.Equal(
            "'shell.url' must not carry credentials (they would be committed); use a deploy key or a CI secret for the clone",
            Error($"version: 2\nshell:\n  url: \"{url}\"\n  unit: a\n"));

    [Theory]
    [InlineData("url: http://example.com/shell.git\n  unit: a", "'shell.url' must be an https:// or SSH URL (git@host:path or ssh://host/path)")]
    [InlineData("url: https://example.com/shell.git", "'shell' needs a 'url' and a 'unit' (this repository's path among the shell's units)")]
    [InlineData("url: https://example.com/shell.git\n  unit: ../a", "'shell.unit' must be a relative path inside the shell, without '..' (for example services/orders)")]
    [InlineData("url: https://example.com/shell.git\n  unit: a\n  ref: \"a..b\"", "'shell.ref' must be a branch name such as main")]
    public void AnInvalidShellSectionIsAnError(string body, string expected) =>
        Assert.Equal(expected, Error($"version: 2\nshell:\n  {body}\n"));

    [Fact]
    public void AShellAndUnitsCannotBothBeSet() =>
        Assert.Equal(
            "'shell' and 'units' cannot both be set: a repository is either a shell or monorepo with units, or a unit of a shell",
            Error("version: 2\nunits:\n  - path: a\nshell:\n  url: https://example.com/shell.git\n  unit: a\n"));
}
