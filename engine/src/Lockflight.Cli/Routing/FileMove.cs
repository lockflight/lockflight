namespace Lockflight.Cli.Routing;

/// <summary>
/// A deleted file and an added file of the same role and project paired as one move: the old and new path, how they were paired,
/// how many counted lines differ (zero for an exact move), and the Dice similarity of their line multisets (1 for an exact move).
/// </summary>
internal sealed record FileMove(string OldPath, string NewPath, MoveKind Kind, int DifferingLines, double Similarity);
