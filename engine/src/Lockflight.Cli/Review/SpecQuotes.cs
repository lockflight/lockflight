using System.Text.RegularExpressions;

namespace Lockflight.Cli.Review;

/// <summary>
/// Checks a finding's spec quote against the spec: anchored when the quote, normalised, is a substring of the spec normalised the
/// same way. Normalising makes every run of whitespace one space, trims the ends and removes Markdown emphasis and code markers:
/// the backticks around a code span (its content is kept as written), and any run of <c>*</c>, <c>_</c> or backticks at the
/// edge of a word, as in <c>**Given**</c> or <c>_stops_</c>. A run inside a word (<c>retry_count</c>, <c>2*3</c>) or between
/// spaces (<c>2 * 3</c>) is not a delimiter and stays, so two texts compare equal only when they differ by such delimiters,
/// never by a letter, a digit, punctuation or an identifier's underscore. Case and punctuation count. A quote of fewer than
/// <see cref="MinimumWords"/> words is not anchored, because a word or two of a spec says nothing about which requirement is meant.
/// </summary>
internal static partial class SpecQuotes
{
    public const int MinimumWords = 3;

    public static SpecAnchor Check(string? quote, string spec)
    {
        if (string.IsNullOrWhiteSpace(quote))
        {
            return SpecAnchor.None;
        }

        string normalised = Normalise(quote);
        return normalised.Split(' ').Length >= MinimumWords && Normalise(spec).Contains(normalised, StringComparison.Ordinal)
            ? SpecAnchor.Anchored
            : SpecAnchor.NotAnchored;
    }

    private static string Normalise(string text) =>
        Delimiters().Replace(Whitespace().Replace(text, " "), match => match.Groups["code"].Value).Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<ticks>`+)(?<code>[^`]+?)\k<ticks>|(?<![\p{L}\p{N}])[*_`]+(?=\S)|(?<=\S)[*_`]+(?![\p{L}\p{N}])")]
    private static partial Regex Delimiters();
}
