namespace Lockflight.Cli.Hooks;

/// <summary>One token of a shell command: its kind and its text (a word after quote removal, or the operator).</summary>
internal sealed record ShellToken(ShellTokenKind Kind, string Text)
{
    public bool IsWord => Kind == ShellTokenKind.Word;

    /// <summary>The index in <see cref="Text"/> of the first character that came from quotes or an escape; null when none did.</summary>
    public int? QuotedFrom { get; init; }

    /// <summary>Whether a <c>$</c> in <see cref="Text"/> came from single quotes or an escape, and so starts no expansion.</summary>
    public bool LiteralDollar { get; init; }
}
