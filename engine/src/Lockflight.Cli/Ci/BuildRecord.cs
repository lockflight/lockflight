namespace Lockflight.Cli.Ci;

/// <summary>One build the judge made, for the evidence's timeline: the commit, why it was built, how long it took and how it ended.</summary>
internal sealed record BuildRecord(string Commit, string Purpose, double Seconds, TreeOutcome Outcome);
