namespace Lockflight.Cli.Ci;

/// <summary>The spellings of <see cref="JudgeEvent"/> on the command line and in evidence: <c>pr</c>, <c>push</c>, <c>merge-group</c>.</summary>
internal static class JudgeEventNames
{
    public const string Accepted = "pr, push or merge-group";

    public static string ToName(JudgeEvent judgeEvent) => judgeEvent switch
    {
        JudgeEvent.PullRequest => "pr",
        JudgeEvent.Push => "push",
        JudgeEvent.MergeGroup => "merge-group",
        _ => throw new ArgumentOutOfRangeException(nameof(judgeEvent), judgeEvent, "Unknown event."),
    };

    /// <summary>The event <paramref name="name"/> spells; null when it spells none.</summary>
    public static JudgeEvent? Parse(string name) => name switch
    {
        "pr" => JudgeEvent.PullRequest,
        "push" => JudgeEvent.Push,
        "merge-group" => JudgeEvent.MergeGroup,
        _ => null,
    };
}
