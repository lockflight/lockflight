namespace Lockflight.Cli.Ci;

/// <summary>The evidence spellings of <see cref="StructuralRoute"/>.</summary>
internal static class StructuralRouteNames
{
    public static string ToName(StructuralRoute route) => route switch
    {
        StructuralRoute.Codeowners => "codeowners",
        StructuralRoute.SpecFirst => "spec-first",
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Unknown structural route."),
    };
}
