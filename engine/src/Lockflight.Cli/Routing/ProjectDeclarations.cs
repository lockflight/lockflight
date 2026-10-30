namespace Lockflight.Cli.Routing;

/// <summary>
/// The declarations of one project's changed C# files at base and at head, and the difference of the two multisets: the
/// declarations the change adds and removes. A declaration moved between files of the project is in both sets and in neither
/// difference; an edited signature is one removed and one added declaration.
/// </summary>
internal sealed record ProjectDeclarations(
    string? Project,
    IReadOnlyList<PlacedDeclaration> AtBase,
    IReadOnlyList<PlacedDeclaration> AtHead,
    IReadOnlyList<PlacedDeclaration> Added,
    IReadOnlyList<PlacedDeclaration> Removed);
