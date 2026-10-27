namespace Lockflight.Cli.Review;

/// <summary>
/// The material a review prompt fences as data: the requirements (the feature's spec), the diff, project guidance read from
/// the base commit (null when the base has none), and the reproduction context read from the working tree (empty when there is none). <see cref="Marker"/> is random per run, so text in the change cannot close a fence.
/// </summary>
internal sealed record ReviewPromptData(string Requirements, string RequirementsSource, string Diff, string? Guidance, string GuidanceSource, string ReproductionContext, string Marker);
