namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// The result of checking a typed session code; when it matched, what the person was shown with it: the subject (a feature, a
/// unit), the named hashes it binds and the entries it carries (what a baseline would accept). While the waiting code stays valid
/// (it matched, or the typed one was wrong but not the last wrong one allowed), <see cref="ExpiresAt"/> says until when and
/// <see cref="AttemptsLeft"/> how many more wrong codes it survives, so a refusal can say the code still works. An expired code
/// keeps its <see cref="Subject"/>, so step 1 can be shown again for what it was shown for.
/// </summary>
internal sealed record SessionCodeCheck(SessionCodeState State, string? Subject, IReadOnlyList<(string Name, string Hash)> Bound, IReadOnlyList<string> Payload)
{
    public DateTimeOffset? ExpiresAt { get; init; }

    public int AttemptsLeft { get; init; }

    public static SessionCodeCheck Of(SessionCodeState state) => new(state, null, [], []);
}
