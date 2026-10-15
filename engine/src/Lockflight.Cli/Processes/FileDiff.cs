namespace Lockflight.Cli.Processes;

/// <summary>One file of a zero-context diff: its new path (as git printed it, unquoted), the lines it adds and the lines it removes.</summary>
internal sealed record FileDiff(string File, IReadOnlyList<DiffLine> Added, IReadOnlyList<DiffLine> Removed)
{
    private const char ByteOrderMark = '﻿';

    /// <summary>
    /// Whether the change adds or removes a UTF-8 byte order mark: line 1 is replaced and only one side starts with one. A BOM is part
    /// of line 1's text in git's diff, so an unchanged line 1 means an unchanged encoding marker.
    /// </summary>
    public bool ChangesByteOrderMark =>
        Added.FirstOrDefault(line => line.Line == 1) is DiffLine added
        && Removed.FirstOrDefault(line => line.Line == 1) is DiffLine removed
        && added.Text.StartsWith(ByteOrderMark) != removed.Text.StartsWith(ByteOrderMark);
}
