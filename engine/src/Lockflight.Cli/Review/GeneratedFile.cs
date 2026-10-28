namespace Lockflight.Cli.Review;

/// <summary>
/// A changed file whose diff the reviewer is not shown because its path marks it as generated (<see cref="GeneratedFiles"/>):
/// its path, its size in bytes and the SHA-256 of its content in the working tree, so the listing pins what was left out.
/// </summary>
internal sealed record GeneratedFile(string Path, long Bytes, string Sha256);
