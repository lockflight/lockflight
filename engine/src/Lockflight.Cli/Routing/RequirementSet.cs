using Lockflight.Cli.Config;

namespace Lockflight.Cli.Routing;

/// <summary>What a change class requires; a kind that is not listed is <see cref="RequirementLevel.Off"/>.</summary>
internal sealed record RequirementSet(ChangeClass Class, IReadOnlyList<Requirement> Items)
{
    public RequirementLevel LevelOf(RequirementKind kind) =>
        Items.FirstOrDefault(item => item.Kind == kind)?.Level ?? RequirementLevel.Off;
}
