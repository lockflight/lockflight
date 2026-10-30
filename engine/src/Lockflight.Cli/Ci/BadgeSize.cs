namespace Lockflight.Cli.Ci;

/// <summary>The size of a verdict badge: one per place a person sees it.</summary>
internal enum BadgeSize
{
    /// <summary>Beside the verdict line of the PR comment (three screen pixels per art pixel).</summary>
    Small = 1,

    /// <summary>At the top of the job summary (eight screen pixels per art pixel).</summary>
    Large = 2,
}
