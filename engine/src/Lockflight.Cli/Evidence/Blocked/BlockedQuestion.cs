namespace Lockflight.Cli.Evidence.Blocked;

/// <summary>A question for a person, as a choice: its options, the recommended one first.</summary>
internal sealed record BlockedQuestion(string Id, string Text, IReadOnlyList<BlockedOption> Options);
