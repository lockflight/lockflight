using System.Text.RegularExpressions;

namespace Lockflight.Cli.Adoption;

/// <summary>
/// Decides what adoption does with a hook, from its event, matcher and script text (keep and flag; replace nothing
/// automatically). A PreToolUse write guard that blocks by path patterns gets those patterns added to <c>protected:</c>; a hook
/// that rewrites files after an edit, or builds at Stop, is flagged; everything else is kept as the team's own control. A command
/// that names <c>$CLAUDE_PROJECT_DIR</c> without quotes is flagged too (a guard keeps its paths): in a project whose path has a
/// space it never runs, and Claude Code reports that only as a non-blocking error.
/// </summary>
internal static partial class HookClassifier
{
    public static AdoptionItem Classify(ClaudeHook hook, string? script)
    {
        AdoptionItem item = ClassifyScript(hook, script);
        if (!UnquotedProjectDir().IsMatch(hook.Command))
        {
            return item;
        }

        const string Unquoted = "its command names $CLAUDE_PROJECT_DIR without quotes, so it does not run when the project path has a space; write \"$CLAUDE_PROJECT_DIR\"/...";
        return item.Action == AdoptionAction.Protect
            ? item with { Reason = $"{item.Reason}; {Unquoted}" }
            : item with { Action = AdoptionAction.Flag, Reason = $"{Unquoted} ({item.Reason})" };
    }

    private static AdoptionItem ClassifyScript(ClaudeHook hook, string? script)
    {
        string subject = $"{hook.Scope}: {hook.Event}{(hook.Matcher is null ? string.Empty : $" [{hook.Matcher}]")} {Path.GetFileName(hook.ScriptPath ?? hook.Command.Trim().Split(' ', 2)[0])}";
        string text = script ?? hook.Command;
        bool writeMatcher = hook.Matcher is not null && WriteTools().IsMatch(hook.Matcher);
        if (hook.Event == "PreToolUse" && writeMatcher && Blocks().IsMatch(text) && GuardPatterns.Extract(text) is { Count: > 0 } globs)
        {
            return new AdoptionItem(AdoptionArea.Hook, subject, AdoptionAction.Protect,
                $"a write guard by path; its paths are added to protected: so Lockflight's guard covers shell commands too (the hook stays): {string.Join(", ", globs)}")
            {
                Globs = globs,
            };
        }

        if (hook.Event == "PostToolUse" && writeMatcher && Rewrites().IsMatch(text))
        {
            return new AdoptionItem(AdoptionArea.Hook, subject, AdoptionAction.Flag,
                "rewrites files after an edit, outside the guard's view: during a red cycle it can rewrite locked tests; Lockflight's format gate checks formatting at Stop and in CI");
        }

        if (hook.Event == "Stop" && Builds().IsMatch(text))
        {
            return new AdoptionItem(AdoptionArea.Hook, subject, AdoptionAction.Flag,
                "builds or tests at Stop in parallel with Lockflight's Stop gate (two builds on the same obj/ folders); keep one");
        }

        if (hook.Scope == "user")
        {
            return new AdoptionItem(AdoptionArea.Hook, subject, AdoptionAction.Keep, "a user-level hook: runs in every project next to Lockflight's; never changed by adoption");
        }

        return new AdoptionItem(AdoptionArea.Hook, subject, AdoptionAction.Keep,
            CommitGuard().IsMatch(text)
                ? "a commit/push confirmation: Lockflight has no equivalent, so it stays (protected from the agent)"
                : "the team's own control: kept, and protected from the agent (.claude/hooks/**)");
    }

    [GeneratedRegex(@"\b(Edit|Write|MultiEdit|NotebookEdit)\b")]
    private static partial Regex WriteTools();

    [GeneratedRegex(@"\bexit\s+2\b|""decision""\s*:\s*""block""|permissionDecision")]
    private static partial Regex Blocks();

    [GeneratedRegex(@"dotnet\s+format|prettier|eslint\s+[^\n]*--fix|black\b|ruff\s+format|gofmt|sed\s+-i")]
    private static partial Regex Rewrites();

    [GeneratedRegex(@"dotnet\s+(build|test)|npm\s+(run\s+)?(build|test)|msbuild")]
    private static partial Regex Builds();

    [GeneratedRegex(@"(?<![""'])\$\{?CLAUDE_PROJECT_DIR\b")]
    private static partial Regex UnquotedProjectDir();

    [GeneratedRegex(@"git\s+(push|commit)")]
    private static partial Regex CommitGuard();
}
