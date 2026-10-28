namespace Lockflight.Cli.Hosting;

/// <summary>
/// What the engine knows about a command line without constructing the command: its words (<c>gate run</c>), who may run it, the
/// options it accepts, what may follow the words, and how to create it. Dispatch, the catalog <c>docs check</c> reads and the
/// human-only refusal are computed from these; the option lists are literals, so building the registry touches no command class.
/// </summary>
internal sealed record CommandDescriptor(
    IReadOnlyList<string> Words,
    CommandAudience Audience,
    IReadOnlyList<string> ValueOptions,
    IReadOnlyList<string> FlagOptions,
    CommandOperands Operands,
    OptionParsing Parsing,
    Func<CliContext, ICommand> Create)
{
    public string Name => string.Join(' ', Words);

    public CommandListing Listing { get; init; } = CommandListing.Listed;

    /// <summary>Whether <paramref name="args"/> begin with this command's words.</summary>
    public bool Begins(IReadOnlyList<string> args) =>
        args.Count >= Words.Count && Words.Select((word, index) => args[index] == word).All(same => same);

    /// <summary>Whether what follows the words is allowed by <see cref="Operands"/>.</summary>
    public bool AcceptsRest(int restCount) => Operands switch
    {
        CommandOperands.None => restCount == 0,
        CommandOperands.ExactlyOne => restCount == 1,
        CommandOperands.AtLeastOne => restCount >= 1,
        _ => true,
    };
}
