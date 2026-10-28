using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;
using Lockflight.Cli.Rules;

namespace Lockflight.Cli.Tests.Rules;

public sealed class RulesAuditTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-rules-");

    [Fact]
    public async Task ImperativeLinesAreRulesAndOtherLinesAreNot()
    {
        Write("CLAUDE.md", """
            # Project

            ## Rules

            - Never edit `secrets/keys.json`.
            - **Always** run the tests before pushing.
            - **Honest claims.** Distinguish guardrails from boundaries; never overstate a control.
            - The build uses the SDK in global.json.

            You MUST keep commits small. This sentence describes the layout.

            ```
            never read inside a fence
            ```
            """);
        await CommitAsync();

        IReadOnlyList<AuditedRule> rules = await AuditAsync();

        Assert.Equal([5, 6, 7, 10], rules.Select(rule => rule.Line));
        Assert.Equal("Always run the tests before pushing.", rules[1].Text);
    }

    [Fact]
    public async Task AWrappedListItemIsOneRuleOnTheLineItsSentenceStarts()
    {
        Write("CLAUDE.md", "- Keep it short. Never edit\n  `lockflight.yaml` or run\n  `lockflight approve spec` yourself.\n- Next item.\n");
        await CommitAsync();

        AuditedRule rule = Assert.Single(await AuditAsync());

        Assert.Equal((1, "Never edit `lockflight.yaml` or run `lockflight approve spec` yourself."), (rule.Line, rule.Text));
        Assert.Equal(RuleStatus.Checked, rule.Status);
        Assert.Contains("human-only refusal", rule.By, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEditBanOnAProtectedPathIsCheckedByTheGuard()
    {
        Write(Manifest.FileName, "protected:\n  - secrets/**\n");
        Write("CLAUDE.md", "- Never edit `secrets/keys.json`.\n- Do not modify `lockflight.yaml`.\n- Never edit `src/Generated.cs`.\n- Always update `CHANGELOG.md`.\n");
        await CommitAsync();

        IReadOnlyList<AuditedRule> rules = await AuditAsync();

        Assert.Equal([RuleStatus.Checked, RuleStatus.Checked, RuleStatus.GuidanceOnly, RuleStatus.GuidanceOnly], rules.Select(rule => rule.Status));
        Assert.Contains("secrets/**", rules[0].By, StringComparison.Ordinal);
        Assert.Contains("protected:", rules[2].Suggestion, StringComparison.Ordinal);
        Assert.Null(rules[3].Suggestion);
    }

    [Fact]
    public async Task WithoutAManifestTheGuardChecksNothing()
    {
        Write("CLAUDE.md", "- Never edit `lockflight.yaml`.\n");
        await CommitAsync();

        AuditedRule rule = Assert.Single(await AuditAsync());

        Assert.Equal(RuleStatus.GuidanceOnly, rule.Status);
    }

    [Fact]
    public async Task ABanOnRunningAHumanOnlyCommandIsCheckedByTheEngineRefusal()
    {
        Write("CLAUDE.md", "- Human-only commands (`approve`, `baseline`) are never run by an agent.\n- Never run `gate run` twice.\n");
        await CommitAsync();

        IReadOnlyList<AuditedRule> rules = await AuditAsync();

        Assert.Equal([RuleStatus.Checked, RuleStatus.GuidanceOnly], rules.Select(rule => rule.Status));
        Assert.Contains("human-only", rules[0].By, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABannedSymbolIsOnlyRelatedAndSaysWhy()
    {
        Write("src/BannedSymbols.txt", "P:System.DateTime.UtcNow;use the injected TimeProvider\n");
        Write("CLAUDE.md", "- Never use `DateTime.UtcNow`.\n- Never call `Console.ReadKey()`.\n");
        await CommitAsync();

        IReadOnlyList<AuditedRule> rules = await AuditAsync();

        Assert.Equal([RuleStatus.Related, RuleStatus.GuidanceOnly], rules.Select(rule => rule.Status));
        Assert.Contains("baseline", rules[0].By, StringComparison.Ordinal);
        Assert.Contains("BannedSymbols.txt", rules[1].Suggestion, StringComparison.Ordinal);
        Assert.Contains("not verified", rules[1].Suggestion, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADependencyBanSuggestsAnArchitectureTest()
    {
        Write(".specify/memory/constitution.md", "### I. Layers\n\nThe domain MUST NOT depend on the infrastructure project.\n");
        await CommitAsync();

        AuditedRule rule = Assert.Single(await AuditAsync());

        Assert.Equal(RuleStatus.GuidanceOnly, rule.Status);
        Assert.Contains("NetArchTest", rule.Suggestion, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SkillsAndRulesFilesAreReadButSpecKitSkillsAndDocsAreNot()
    {
        Write(".claude/skills/release/SKILL.md", "- Always tag the release.\n");
        Write(".claude/rules/api.md", "- Never return null from a controller.\n");
        Write(".claude/skills/speckit-plan/SKILL.md", "- You MUST plan.\n");
        Write("docs/guide.md", "- Never do this.\n");
        await CommitAsync();

        IReadOnlyList<AuditedRule> rules = await AuditAsync();

        Assert.Equal([".claude/rules/api.md", ".claude/skills/release/SKILL.md"], rules.Select(rule => rule.File).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheCommandGroupsRulesPerFileAndNeverSaysEnforced()
    {
        Write(Manifest.FileName, "protected:\n  - secrets/**\n");
        Write("CLAUDE.md", "- Never edit `secrets/a.json`.\n- Always be kind.\n");
        await CommitAsync();

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["rules", "audit"], string.Empty, root.FullName);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("CLAUDE.md (2 rules)", output, StringComparison.Ordinal);
        Assert.Contains("checked (G)", output, StringComparison.Ordinal);
        Assert.Contains("guidance only", output, StringComparison.Ordinal);
        Assert.DoesNotContain("enforced", output, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<AuditedRule>> AuditAsync() =>
        (await RulesAudit.RunAsync(root.FullName, TestContext.Current.CancellationToken)).Rules;

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(root.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private async Task CommitAsync()
    {
        await GitAsync(["init", "--quiet", "--initial-branch=main"]);
        await GitAsync(["add", "--all"]);
        await GitAsync(["-c", "user.name=Test", "-c", "user.email=test@example.com", "commit", "--quiet", "-m", "rules"]);
    }

    private async Task GitAsync(string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, root.FullName, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Combined);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
