namespace Lockflight.Cli.Routing;

/// <summary>The JSON spellings of <see cref="RequirementKind"/> and <see cref="RequirementLevel"/>.</summary>
internal static class RequirementNames
{
    public static string ToName(RequirementKind kind) => kind switch
    {
        RequirementKind.Gates => "gates",
        RequirementKind.PatchCoverage => "patchCoverage",
        RequirementKind.Suppressions => "suppressions",
        RequirementKind.DocsReferences => "docsReferences",
        RequirementKind.EfPendingModelChanges => "efPendingModelChanges",
        RequirementKind.RedGreen => "redGreen",
        RequirementKind.SpecAndTrace => "specAndTrace",
        RequirementKind.Review => "review",
        RequirementKind.ArchitectureTests => "architectureTests",
        RequirementKind.SpecFirst => "specFirst",
        RequirementKind.Mutation => "mutation",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown requirement kind."),
    };

    public static string ToName(RequirementLevel level) => level switch
    {
        RequirementLevel.Off => "off",
        RequirementLevel.WhenApplicable => "whenApplicable",
        RequirementLevel.Required => "required",
        RequirementLevel.RequiredWithKey => "requiredWithKey",
        RequirementLevel.Advisory => "advisory",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown requirement level."),
    };
}
