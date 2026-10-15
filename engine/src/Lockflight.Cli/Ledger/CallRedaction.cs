using System.Text.RegularExpressions;

namespace Lockflight.Cli.Ledger;

/// <summary>
/// Shortens a blocked call for the ledger and masks what looks like a credential: a run of 24 or more token characters, or the
/// value after <c>Bearer</c>, <c>token</c>, <c>password</c>, <c>secret</c> or <c>key</c>. Best effort; the ledger is local and
/// git-ignored, but the agent can read it.
/// </summary>
internal static partial class CallRedaction
{
    public const int MaxLength = 200;

    public static string Shorten(string call)
    {
        string masked = Labelled().Replace(LongToken().Replace(call, "***"), match => $"{match.Groups["label"].Value}***");
        string oneLine = masked.ReplaceLineEndings(" ");
        return oneLine.Length > MaxLength ? oneLine[..MaxLength] + "…" : oneLine;
    }

    [GeneratedRegex(@"[A-Za-z0-9_\-]{24,}")]
    private static partial Regex LongToken();

    [GeneratedRegex(@"(?<label>(?i:bearer\s+|(?:token|password|passwd|secret|api[_-]?key|key)\s*[=:]\s*))\S+")]
    private static partial Regex Labelled();
}
