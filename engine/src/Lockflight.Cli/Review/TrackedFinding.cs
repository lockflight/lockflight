namespace Lockflight.Cli.Review;

/// <summary>A tracked finding as last recorded, with the run that first opened it and the last run that recorded it.</summary>
internal sealed record TrackedFinding(ReviewFinding Finding, string FirstRun, string LastRun);
