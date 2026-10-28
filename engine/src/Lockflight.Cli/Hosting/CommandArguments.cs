namespace Lockflight.Cli.Hosting;

/// <summary>
/// What a command is invoked with: the descriptor dispatch matched (its words and the options it accepts) and everything after
/// those words, already checked against the descriptor's <see cref="CommandDescriptor.Operands"/>.
/// </summary>
internal sealed record CommandArguments(CommandDescriptor Command, IReadOnlyList<string> Rest)
{
    /// <summary>The words after the command's own, as the array the option parser and the commands read.</summary>
    public string[] RestArray => [.. Rest];

    /// <summary>
    /// The verb of a command group and what follows it: the descriptor's second word (<c>approve spec</c>), or for a group's
    /// catch-all (<c>approve xyz</c>, <see cref="CommandListing.Unlisted"/>) the first operand, which dispatch guarantees.
    /// </summary>
    public CommandVerb Verb() => Command.Words.Count > 1 ? new(Command.Words[1], RestArray) : new(Rest[0], [.. Rest.Skip(1)]);
}
