namespace Lockflight.Cli.Processes;

/// <summary>A line a diff adds or removes: its 1-based number in the new file (added) or the old file (removed), and its text.</summary>
internal sealed record DiffLine(int Line, string Text);
