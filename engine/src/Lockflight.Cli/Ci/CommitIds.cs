namespace Lockflight.Cli.Ci;

/// <summary>What the judge accepts as a commit id on its command line: a full SHA-1 (40) or SHA-256 (64) hex id in lower case.</summary>
internal static class CommitIds
{
    private const int Sha1Length = 40;
    private const int Sha256Length = 64;

    public static bool IsFull(string text) =>
        text.Length is Sha1Length or Sha256Length && text.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f');

    /// <summary>Whether <paramref name="text"/> is the all-zero id a push event gives for "no previous commit".</summary>
    public static bool IsZero(string text) => IsFull(text) && text.All(character => character == '0');

    /// <summary>The first seven characters, for messages.</summary>
    public static string Short(string commit) => commit.Length > 7 ? commit[..7] : commit;
}
