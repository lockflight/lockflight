namespace Lockflight.Cli.Adoption;

/// <summary>One line of an adoption plan: what was found, what init does with it, why, and the paths it adds to <c>protected:</c>.</summary>
internal sealed record AdoptionItem(AdoptionArea Area, string Subject, AdoptionAction Action, string Reason)
{
    public IReadOnlyList<string> Globs { get; init; } = [];
}
