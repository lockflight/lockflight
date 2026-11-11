using System.Text.RegularExpressions;

namespace Lockflight.Cli.Ledger;

/// <summary>
/// Shortens a blocked call for the ledger and masks what looks like a credential: the password in a URL
/// (<c>https://user:***@host</c>), a random-looking base64 run that may hold <c>/</c> and <c>+</c> (an AWS secret key), a run of
/// 24 or more token characters, the value after <c>Bearer</c>, <c>token</c>, <c>password</c>, <c>secret</c> or <c>key</c> with
/// <c>=</c> or <c>:</c>, the value after a long option or variable named for one (<c>--password x</c>,
/// <c>aws_secret_access_key x</c>), the credentials of an <c>Authorization: Basic</c> header, the password of
/// <c>curl -u user:password</c>, and a MySQL or MariaDB client's <c>-psecret</c>. Best effort; the ledger is local and
/// git-ignored, but the agent can read it.
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

    public static string Shorten(string call) => OneLine(Masked(call));

    /// <summary>The text with what looks like a credential masked, at its full length: a block's reason, which may quote the call.</summary>
    public static string Masked(string text)
    {
        string masked = UrlPassword().Replace(text, match => $"{match.Groups["prefix"].Value}{Mask}@");
        masked = UserPassword().Replace(masked, match => $"{match.Groups["label"].Value}{Mask}");
        masked = MySqlPassword().Replace(masked, match => $"{match.Groups["label"].Value}{Mask}");
        masked = Base64Run().Replace(masked, match => LooksRandom(match.Value) ? Mask : match.Value);
        masked = LongToken().Replace(masked, Mask);
        masked = Labelled().Replace(masked, match => $"{match.Groups["label"].Value}{Mask}");
        return masked;
    }

    /// <summary>
    /// Shortens a file path without masking: the credential patterns would take a long path of random-looking folder names
    /// (<c>/var/folders/…/T/…</c> on macOS) for a key and hide the whole path.
    /// </summary>
    public static string ShortenPath(string path) => OneLine(path);

    private static string OneLine(string text)
    {
        string oneLine = text.ReplaceLineEndings(" ");
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

    /// <summary><c>curl -u user:password</c>, <c>--user user:password</c>, <c>--proxy-user user:password</c>.</summary>
    [GeneratedRegex(@"(?<label>(?:^|\s)(?:-u\s*|--(?:proxy-)?user[=\s]\s*)['""]?[^\s:'""]*:)[^\s'""]+")]
    private static partial Regex UserPassword();

    /// <summary>The password a MySQL or MariaDB client takes glued to <c>-p</c>, as no other tool's <c>-p</c> is a secret.</summary>
    [GeneratedRegex(@"(?<label>\b(?:mysql|mysqldump|mysqladmin|mariadb|mariadb-dump)\b[^|;&\n]*?\s-p)[^\s|;&]+")]
    private static partial Regex MySqlPassword();

    [GeneratedRegex(@"(?<label>(?i:bearer\s+|authorization:\s*basic\s+|(?:token|password|passwd|secret|api[_-]?key|key)\s*[=:]\s*|--[a-z0-9-]*(?:token|password|passwd|secret|api-?key)\s+|\b\w*(?:secret_access_key|_secret|_token|_password)\s+))\S+")]
    private static partial Regex Labelled();
}
