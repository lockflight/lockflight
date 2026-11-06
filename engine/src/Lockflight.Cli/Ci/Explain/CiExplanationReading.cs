namespace Lockflight.Cli.Ci.Explain;

/// <summary>A file as <c>ci explain</c> read it: the <see cref="Explanation"/>, or the <see cref="Problem"/> that kept it from one.</summary>
internal sealed record CiExplanationReading(CiExplanation? Explanation, string? Problem);
