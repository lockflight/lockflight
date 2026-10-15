namespace Lockflight.Cli.Hosting;

/// <summary>Quotes words for a POSIX shell (and Git Bash), so a command line printed for a person can be pasted as it is.</summary>
internal static class ShellQuoting
{
    private const string PlainCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-./:=,@%+";

    /// <summary>The word in single quotes; an embedded single quote becomes <c>'\''</c>.</summary>
    public static string Quote(string word) => "'" + word.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    /// <summary>The word as it is when the shell would not split or expand it, else <see cref="Quote"/>d.</summary>
    public static string QuoteIfNeeded(string word) =>
        word.Length > 0 && word.All(character => PlainCharacters.Contains(character, StringComparison.Ordinal)) ? word : Quote(word);
}
