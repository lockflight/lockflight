namespace Lockflight.Cli.Routing;

/// <summary>One record of <c>git diff --raw -z --no-abbrev</c>: the modes and blob ids on each side, the status letter and the path.</summary>
internal sealed record RawDiffEntry(string OldMode, string NewMode, string OldBlob, string NewBlob, char Status, string Path);
