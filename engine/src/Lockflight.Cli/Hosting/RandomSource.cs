using System.Security.Cryptography;

namespace Lockflight.Cli.Hosting;

/// <summary>
/// One-time codes from the operating system's cryptographically secure generator. A test may fix the codes it hands out, so it
/// knows the code a person would read.
/// </summary>
internal sealed class RandomSource(Queue<string>? fixedCodes)
{
    public static RandomSource Secure { get; } = new(null);

    /// <summary>A source whose codes are <paramref name="codes"/>, in order.</summary>
    public static RandomSource WithCodes(params IEnumerable<string> codes) => new(new Queue<string>(codes));

    /// <summary>A code of <paramref name="length"/> characters drawn uniformly from <paramref name="alphabet"/>.</summary>
    public string Code(string alphabet, int length) =>
        fixedCodes is null ? RandomNumberGenerator.GetString(alphabet, length) : fixedCodes.Dequeue();
}
