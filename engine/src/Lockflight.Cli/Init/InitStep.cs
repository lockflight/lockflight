namespace Lockflight.Cli.Init;

/// <summary>One file <c>lockflight init</c> looked after, relative to the project root.</summary>
internal sealed record InitStep(string RelativePath, FileOutcome Outcome);
