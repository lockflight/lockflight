namespace Lockflight.Cli.Evidence.Blocked;

/// <summary>
/// One thing a person must do (<c>needs[]</c>). It names no command: a file an agent or a crew skill writes cannot be trusted to
/// carry one, so whatever renders the report derives the command from the reason, the action and the feature.
/// </summary>
/// <param name="WhoName">The value as written, shown as it is when <see cref="Who"/> is unknown.</param>
/// <param name="ActionName">The value as written, shown as it is when <see cref="Action"/> is unknown.</param>
internal sealed record BlockedNeed(BlockedWho Who, string WhoName, BlockedAction Action, string ActionName, string Detail, bool HumanOnly)
{
    public IReadOnlyList<string> Paths { get; init; } = [];

    public static BlockedNeed Of(BlockedWho who, BlockedAction action, string detail, bool humanOnly) =>
        new(who, BlockedNames.ToName(who), action, BlockedNames.ToName(action), detail, humanOnly);
}
