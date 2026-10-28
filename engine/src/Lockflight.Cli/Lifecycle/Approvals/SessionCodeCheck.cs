namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// The result of checking a typed session code; when it matched, what the person was shown with it: the subject (a feature, a
/// unit), the named hashes it binds and the entries it carries (what a baseline would accept).
/// </summary>
internal sealed record SessionCodeCheck(SessionCodeState State, string? Subject, IReadOnlyList<(string Name, string Hash)> Bound, IReadOnlyList<string> Payload)
{
    public static SessionCodeCheck Of(SessionCodeState state) => new(state, null, [], []);
}
