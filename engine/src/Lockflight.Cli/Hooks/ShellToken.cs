namespace Lockflight.Cli.Hooks;

/// <summary>One token of a shell command: its kind and its text (a word after quote removal, or the operator).</summary>
internal sealed record ShellToken(ShellTokenKind Kind, string Text)
{
    public bool IsWord => Kind == ShellTokenKind.Word;
}
