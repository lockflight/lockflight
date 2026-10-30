using System.Globalization;

namespace Lockflight.Cli.Evidence;

/// <summary>
/// The one timestamp form the evidence the engine writes uses: RFC 3339 in UTC with <c>Z</c>, to the second
/// (<c>2026-11-02T10:15:00Z</c>). Readers also accept the <c>+00:00</c> round-trip form older files hold.
/// </summary>
internal static class EvidenceTime
{
    private const string WrittenFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public static string Format(DateTimeOffset time) => time.UtcDateTime.ToString(WrittenFormat, CultureInfo.InvariantCulture);

    /// <summary>The instant <paramref name="text"/> names, in either form; null when it is not an RFC 3339 time with an offset.</summary>
    public static DateTimeOffset? Parse(string? text) =>
        text is not null
        && (text.EndsWith('Z') || text.Length > 6 && (text[^6] is '+' or '-'))
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset time)
            ? time.ToUniversalTime()
            : null;
}
