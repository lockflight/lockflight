using Lockflight.Cli.Adoption;

namespace Lockflight.Cli.Tests.Adoption;

public sealed class HookClassifierTests
{
    [Fact]
    public void AWriteGuardByPathIsProtectedAndKept()
    {
        const string script = """
            input=$(cat)
            case "$input" in
              *docker-compose.override.yml*|*"/Contracts/"*)
                echo "no" >&2; exit 2 ;;
            esac
            """;

        AdoptionItem item = HookClassifier.Classify(Hook("PreToolUse", "Edit|Write"), script);

        Assert.Equal(AdoptionAction.Protect, item.Action);
        Assert.Equal(["**/docker-compose.override.yml", "**/Contracts/**"], item.Globs);
    }

    [Fact]
    public void AFormatterAfterEditsIsFlagged()
    {
        AdoptionItem item = HookClassifier.Classify(Hook("PostToolUse", "Edit|Write"), "dotnet format whitespace --include \"$file\"");

        Assert.Equal(AdoptionAction.Flag, item.Action);
        Assert.Contains("locked tests", item.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AStopBuildIsFlaggedAndACommitConfirmationKept()
    {
        Assert.Equal(AdoptionAction.Flag, HookClassifier.Classify(Hook("Stop", matcher: null), "dotnet build x.sln").Action);
        AdoptionItem confirm = HookClassifier.Classify(Hook("PreToolUse", "Bash"), "case \"$input\" in *\"git push\"*) exit 2 ;; esac");
        Assert.Equal(AdoptionAction.Keep, confirm.Action);
        Assert.Contains("no equivalent", confirm.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void PatternsWithVariablesOrClassesAreLeftToTheHook()
    {
        Assert.Empty(GuardPatterns.Extract("case \"$f\" in\n  *$SECRET*|*[0-9].env*) exit 2 ;;\nesac"));
    }

    [Theory]
    [InlineData("$CLAUDE_PROJECT_DIR/.claude/hooks/notify.sh", true)]
    [InlineData("${CLAUDE_PROJECT_DIR}/.claude/hooks/notify.sh", true)]
    [InlineData("\"$CLAUDE_PROJECT_DIR\"/.claude/hooks/notify.sh", false)]
    [InlineData("\"$CLAUDE_PROJECT_DIR/.claude/hooks/notify.sh\"", false)]
    public void AnUnquotedProjectDirectoryIsFlaggedBecauseASpaceInThePathStopsTheHook(string command, bool flagged)
    {
        AdoptionItem item = HookClassifier.Classify(new ClaudeHook("shell", "Stop", null, command, ScriptPath: null), "notify-send done");

        Assert.Equal(flagged ? AdoptionAction.Flag : AdoptionAction.Keep, item.Action);
        Assert.Equal(flagged, item.Reason.Contains("without quotes", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnquotedGuardStillAddsItsPathsAndSaysItDoesNotRun()
    {
        const string script = """
            input=$(cat)
            case "$input" in
              *docker-compose.override.yml*)
                echo "no" >&2; exit 2 ;;
            esac
            """;

        AdoptionItem item = HookClassifier.Classify(new ClaudeHook("shell", "PreToolUse", "Edit|Write", "$CLAUDE_PROJECT_DIR/.claude/hooks/guard.sh", ScriptPath: null), script);

        Assert.Equal(AdoptionAction.Protect, item.Action);
        Assert.Equal(["**/docker-compose.override.yml"], item.Globs);
        Assert.Contains("without quotes", item.Reason, StringComparison.Ordinal);
    }

    private static ClaudeHook Hook(string hookEvent, string? matcher) => new("shell", hookEvent, matcher, "/x/hook.sh", ScriptPath: null);
}
