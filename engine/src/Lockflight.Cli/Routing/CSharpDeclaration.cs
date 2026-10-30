namespace Lockflight.Cli.Routing;

/// <summary>
/// One C# declaration without its body: the path of the types around it (<c>Shop.Api.CartController</c>), what it declares, its
/// name, its header with whitespace collapsed (modifiers, return type, parameters, base list; string contents kept), its
/// attributes (each collapsed, sorted), the base list of a type (after its <c>:</c>, empty otherwise), its 1-based line, and
/// whether it is reachable from outside its assembly as written (<c>public</c> or <c>protected</c>, or a member of an interface).
/// </summary>
internal sealed record CSharpDeclaration(
    string Container, DeclarationKind Kind, string Name, string Signature, IReadOnlyList<string> Attributes, string BaseList, int Line, bool PublicSurface)
{
    /// <summary>What two declarations must share to be the same declaration: place, header and attributes, not the line.</summary>
    public string Key => $"{Container}\u0001{Signature}\u0001{string.Join('\u0001', Attributes)}";

    /// <summary>The full name of a type declaration (container and name); for a member, its container.</summary>
    public string TypePath => Kind == DeclarationKind.Type ? (Container.Length == 0 ? Name : $"{Container}.{Name}") : Container;
}
