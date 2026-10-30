namespace Lockflight.Cli.Routing;

/// <summary>A declaration and the file it is in on its side of the change.</summary>
internal sealed record PlacedDeclaration(CSharpDeclaration Declaration, string Path)
{
    public string Where => $"{Path}:{Declaration.Line}";
}
