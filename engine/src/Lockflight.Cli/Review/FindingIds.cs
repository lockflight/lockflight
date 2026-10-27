using System.Security.Cryptography;
using System.Text;

namespace Lockflight.Cli.Review;

/// <summary>
/// A finding's id: the start of a SHA-256 over its dimension, file, line range and claim (the title, lowercased and with runs of
/// whitespace collapsed), so the same claim about the same place keeps its id from run to run.
/// </summary>
internal static class FindingIds
{
    private const int Length = 12;

    public static string Of(string dimension, string file, int line, string title)
    {
        string claim = string.Join(' ', title.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        string key = $"{dimension}\n{file.Replace('\\', '/')}\n{line}-{line}\n{claim}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..Length];
    }
}
