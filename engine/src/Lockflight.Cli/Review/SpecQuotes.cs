using System.Text.RegularExpressions;

namespace Lockflight.Cli.Review;

/// <summary>
/// Checks a finding's spec quote against the spec: anchored when the quote, with every run of whitespace made one space and the
/// ends trimmed, is a substring of the spec normalised the same way. Case and punctuation count. A quote of fewer than
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

    private static string Normalise(string text) => Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
