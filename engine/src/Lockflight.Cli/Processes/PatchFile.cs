namespace Lockflight.Cli.Processes;

/// <summary>
/// One file of a zero-context diff, deleted files included: its old path (null when the file is added), its new path (null when it
/// is deleted), the lines it adds (numbered in the new file) and the lines it removes (numbered in the old file).
/// </summary>
internal sealed record PatchFile(string? OldFile, string? NewFile, IReadOnlyList<DiffLine> Added, IReadOnlyList<DiffLine> Removed);
