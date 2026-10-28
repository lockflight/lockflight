using System.Text.RegularExpressions;

namespace Lockflight.Cli.Ledger;

/// <summary>
/// Shortens a blocked call for the ledger and masks what looks like a credential: the password in a URL
/// (<c>https://user:***@host</c>), a random-looking base64 run that may hold <c>/</c> and <c>+</c> (an AWS secret key), a run of
/// 24 or more token characters, the value after <c>Bearer</c>, <c>token</c>, <c>password</c>, <c>secret</c> or <c>key</c> with
/// <c>=</c> or <c>:</c>, and the value after a long option or variable named for one (<c>--password x</c>,
/// <c>aws_secret_access_key x</c>). Best effort; the ledger is local and git-ignored, but the agent can read it.
/// </summary>
internal static partial class CallRedaction
{
    public const int MaxLength = 200;

    private const string Mask = "***";

    /// <summary>
    /// The least share of neighbouring characters inside a segment that change class (lower, upper, digit) in a random-looking run:
    /// about 0.6 for random base64, under 0.25 for paths and CamelCase names.
    /// </summary>
    private const double RandomClassChanges = 0.3;

    private static readonly char[] Separators = ['/', '_', '-', '='];

    public static string Shorten(string call)
    {
        string masked = UrlPassword().Replace(call, match => $"{match.Groups["prefix"].Value}{Mask}@");
        masked = Base64Run().Replace(masked, match => LooksRandom(match.Value) ? Mask : match.Value);
        masked = LongToken().Replace(masked, Mask);
        masked = Labelled().Replace(masked, match => $"{match.Groups["label"].Value}{Mask}");
        string oneLine = masked.ReplaceLineEndings(" ");
        return oneLine.Length > MaxLength ? oneLine[..MaxLength] + "…" : oneLine;
    }

    /// <summary>
    /// Whether a run looks generated rather than written: a <c>+</c> (rare in paths and names), or letters of both cases and digits
    /// that change class often inside their segments (<c>wJalrXUtnFEMI/K7MDENG</c>), unlike a path or a CamelCase name
    /// (<c>Projects/MyApp2/src</c>).
    /// </summary>
    private static bool LooksRandom(string run)
    {
        if (run.Contains('+', StringComparison.Ordinal))
        {
            return true;
        }

        string[] segments = run.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        string letters = string.Concat(segments);
        if (!letters.Any(char.IsAsciiDigit) || !letters.Any(char.IsAsciiLetterUpper) || !letters.Any(char.IsAsciiLetterLower))
        {
            return false;
        }

        int pairs = segments.Sum(segment => segment.Length - 1);
        int changes = segments.Sum(segment => segment.Skip(1).Where((character, index) => ClassOf(character) != ClassOf(segment[index])).Count());
        return pairs > 0 && changes >= RandomClassChanges * pairs;
    }

    private static int ClassOf(char character) => char.IsAsciiDigit(character) ? 0 : char.IsAsciiLetterUpper(character) ? 1 : 2;

    [GeneratedRegex(@"(?<prefix>://[^/\s:@]*:)[^/\s@]+@")]
    private static partial Regex UrlPassword();

    [GeneratedRegex(@"(?<![A-Za-z0-9+/_\-])[A-Za-z0-9+/_\-]{30,}={0,2}")]
    private static partial Regex Base64Run();

    [GeneratedRegex(@"[A-Za-z0-9_\-]{24,}")]
    private static partial Regex LongToken();

    [GeneratedRegex(@"(?<label>(?i:bearer\s+|(?:token|password|passwd|secret|api[_-]?key|key)\s*[=:]\s*|--[a-z0-9-]*(?:token|password|passwd|secret|api-?key)\s+|\b\w*(?:secret_access_key|_secret|_token|_password)\s+))\S+")]
    private static partial Regex Labelled();
}
