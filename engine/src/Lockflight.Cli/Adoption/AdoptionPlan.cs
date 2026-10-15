namespace Lockflight.Cli.Adoption;

/// <summary>What <c>lockflight adopt plan</c> found in a project and what <c>adopt apply</c> will do with each part of it.</summary>
internal sealed record AdoptionPlan(string Root, DateTimeOffset At, IReadOnlyList<AdoptionItem> Items)
{
    public const string RelativeDirectory = ".lockflight/adopt";

    public const string MarkdownFile = "plan.md";
}
