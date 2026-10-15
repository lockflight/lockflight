namespace Lockflight.Cli.Adoption;

/// <summary>What an undo did: files restored and removed, and files left as they are because they changed after apply.</summary>
internal sealed record UndoResult(int Restored, int Removed, IReadOnlyList<string> Kept);
