using System.Text.RegularExpressions;
using Lockflight.Cli.Config;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Routing;

/// <summary>
/// API surface and new types, from declaration sets compared per project (<see cref="DeclarationSets"/>), never from diff lines, so
/// a reflow, a reorder, a member moved within its project or a <c>public</c> inside a comment or string fires nothing:
/// <list type="bullet">
/// <item><c>type.added</c>: a production project's head declarations hold a type its base lacks (<c>behaviour</c>);</item>
/// <item><c>api.endpointAttribute</c>, <c>api.controllerAction</c>, <c>api.controllerAdded</c>: routing attributes, public controller
/// actions and new controllers, hubs or gRPC services added or removed (<c>feature</c>);</item>
/// <item><c>api.publicMember</c>: in an API project (<see cref="ApiProjects"/>), a public or protected declaration added or removed
/// (<c>feature</c>);</item>
/// <item><c>api.minimalEndpoint</c>: a <c>.Map…(</c> registration added, removed or changed (statement multisets);</item>
/// <item><c>api.pageDirective</c>: a Razor <c>@page</c>, <c>@rendermode</c>, <c>[Route(</c> or <c>@attribute [Authorize</c> line
/// added or removed;</item>
/// <item><c>api.contractFile</c>: a <c>.proto</c>, OpenAPI, Swagger or GraphQL contract changed.</item>
/// </list>
/// A body edit in a controller is not a feature. All heuristics.
/// </summary>
internal sealed partial class ApiSignals : IChangeSignalSource
{
    private const string ControllerSuffix = "Controller";

    private static readonly FileRole[] ProductionRoles = [FileRole.Production];
    private static readonly FileRole[] ApiRoles = [FileRole.Production, FileRole.NonProduction];
    private static readonly string[] ContractGlobs = ["**/*.proto", "**/openapi*.json", "**/openapi*.yaml", "**/openapi*.yml", "**/swagger*.json", "**/swagger*.yaml", "**/*.graphql", "**/*.graphqls"];

    public IEnumerable<ChangeSignal> Find(ChangeAnalysis analysis, RoutingPolicy policy)
    {
        foreach (ProjectDeclarations project in DeclarationSets.Of(analysis, ProductionRoles))
        {
            HashSet<string> baseTypes = new(project.AtBase.Where(IsType).Select(placed => placed.Declaration.TypePath), StringComparer.Ordinal);
            if (project.AtHead.Where(IsType).FirstOrDefault(placed => !baseTypes.Contains(placed.Declaration.TypePath)) is PlacedDeclaration type)
            {
                yield return Signal(SignalIds.TypeAdded, ChangeClass.Behaviour, type.Where, $"new type {type.Declaration.TypePath}");
            }
        }

        foreach (ProjectDeclarations project in DeclarationSets.Of(analysis, ApiRoles))
        {
            foreach (ChangeSignal signal in DeclarationSignals(analysis, policy, project))
            {
                yield return signal;
            }
        }

        if (StatementSets.FirstDifference(analysis, ApiRoles, MinimalEndpointCall()) is string mapping)
        {
            yield return Signal(SignalIds.ApiMinimalEndpoint, ChangeClass.Feature, mapping, "an endpoint mapping added, removed or changed");
        }

        foreach (RoutedFile file in analysis.Files.Where(file => file.Move?.Kind != MoveKind.Exact))
        {
            if (file.Role == FileRole.Markup && file.Move?.OldPath != file.Path)
            {
                (IReadOnlyList<RoutedLine> added, IReadOnlyList<RoutedLine> removed) = EffectiveLines.Of(analysis, file);
                if (added.Concat(removed).FirstOrDefault(line => PageDirectiveLine().IsMatch(line.Text)) is RoutedLine directive)
                {
                    yield return Signal(SignalIds.ApiPageDirective, ChangeClass.Feature, $"{file.Path}:{directive.Line}", "a page route, render mode or authorization directive added or removed");
                }
            }

            if (file.Role != FileRole.Test && ContractGlobs.FirstOrDefault(glob => PathRules.Matches(glob, file.Path)) is string contract)
            {
                yield return Signal(SignalIds.ApiContractFile, ChangeClass.Feature, file.Path, $"API contract file changed ({contract})");
            }
        }
    }

    private static IEnumerable<ChangeSignal> DeclarationSignals(ChangeAnalysis analysis, RoutingPolicy policy, ProjectDeclarations project)
    {
        IEnumerable<(PlacedDeclaration Placed, string Change)> changed = project.Added.Select(placed => (placed, "added"))
            .Concat(project.Removed.Select(placed => (placed, "removed")));
        HashSet<string> controllers = Controllers(project);
        bool apiProject = project.Project is string projectFile && ApiProjects.IsApiProject(analysis, policy, projectFile)
            && !PublicApiFiles.Enforced(analysis, projectFile);
        HashSet<string> baseTypes = new(project.AtBase.Where(IsType).Select(placed => placed.Declaration.TypePath), StringComparer.Ordinal);
        HashSet<string> types = new(project.AtBase.Concat(project.AtHead).Where(IsType).Select(placed => placed.Declaration.TypePath), StringComparer.Ordinal);
        HashSet<string> publicTypes = new(
            project.AtBase.Concat(project.AtHead).Where(placed => IsType(placed) && (placed.Declaration.PublicSurface || IsUnmarkedPartial(placed.Declaration))).Select(placed => placed.Declaration.TypePath),
            StringComparer.Ordinal);
        foreach ((PlacedDeclaration placed, string change) in changed)
        {
            CSharpDeclaration declaration = placed.Declaration;
            if (declaration.Attributes.FirstOrDefault(attribute => RoutingAttribute().IsMatch(attribute)) is string attribute)
            {
                yield return Signal(SignalIds.ApiEndpointAttribute, ChangeClass.Feature, placed.Where, $"{attribute} on {declaration.Name} {change}");
            }

            if (declaration.Kind == DeclarationKind.Type && change == "added" && IsEndpointType(declaration) && !baseTypes.Contains(declaration.TypePath))
            {
                yield return Signal(SignalIds.ApiControllerAdded, ChangeClass.Feature, placed.Where, $"new endpoint type {declaration.TypePath}");
            }
            else if (declaration.Kind == DeclarationKind.Type && controllers.Contains(declaration.TypePath))
            {
                yield return Signal(SignalIds.ApiControllerAction, ChangeClass.Feature, placed.Where, $"endpoint type {declaration.TypePath} declaration {change} (its attributes or base)");
            }
            else if (IsAction(declaration, controllers))
            {
                yield return Signal(SignalIds.ApiControllerAction, ChangeClass.Feature, placed.Where, $"public action {declaration.Container}.{declaration.Name} {change}");
            }

            if (apiProject && declaration.PublicSurface && EnclosingTypesPublic(declaration.Container, types, publicTypes))
            {
                yield return Signal(SignalIds.ApiPublicMember, ChangeClass.Feature, placed.Where, $"public {(declaration.Kind == DeclarationKind.Type ? "type" : "member")} {declaration.TypePath}{(declaration.Kind == DeclarationKind.Member ? "." + declaration.Name : string.Empty)} {change} in API project {project.Project}");
            }
        }

        foreach (RoutedFile file in analysis.Files.Where(file => file.Change.Kind == ChangeKind.Added && file.Move is null && file.Role == FileRole.Production))
        {
            if (ProjectIndex.FileName(file.Path).EndsWith(ControllerSuffix + ".cs", StringComparison.OrdinalIgnoreCase) && file.AtHead?.Project == project.Project)
            {
                yield return Signal(SignalIds.ApiControllerAdded, ChangeClass.Feature, file.Path, "new controller file");
            }
        }
    }

    /// <summary>
    /// The type paths of the project's endpoint types on either side: named <c>…Controller</c>, marked <c>[ApiController]</c>,
    /// <c>[Controller]</c> or <c>[Route]</c>, deriving from a <c>…Controller</c> or <c>…ControllerBase</c> type, or a SignalR hub.
    /// </summary>
    private static HashSet<string> Controllers(ProjectDeclarations project) =>
        new(project.AtBase.Concat(project.AtHead)
            .Where(placed => placed.Declaration.Kind == DeclarationKind.Type
                && (placed.Declaration.Name.EndsWith(ControllerSuffix, StringComparison.Ordinal)
                    || ControllerBase().IsMatch(placed.Declaration.BaseList)
                    || HubBase().IsMatch(placed.Declaration.BaseList)
                    || placed.Declaration.Attributes.Any(attribute => ControllerAttribute().IsMatch(attribute))))
            .Select(placed => placed.Declaration.TypePath), StringComparer.Ordinal);

    /// <summary>A part of a partial type with no access modifier takes the other parts' accessibility, which may be public.</summary>
    private static bool IsUnmarkedPartial(CSharpDeclaration declaration)
    {
        string[] words = declaration.Signature.Split([' ', '(', '<', ':'], StringSplitOptions.RemoveEmptyEntries);
        return words.Contains("partial") && !words.Any(word => word is "public" or "internal" or "private" or "protected" or "file");
    }

    private static bool IsAction(CSharpDeclaration declaration, HashSet<string> controllers) =>
        declaration.Kind == DeclarationKind.Member
        && controllers.Contains(declaration.Container)
        && declaration.Signature.Split(' ').Contains("public")
        && declaration.Signature.Contains('(', StringComparison.Ordinal)
        && declaration.Name != declaration.Container[(declaration.Container.LastIndexOf('.') + 1)..];

    private static bool IsEndpointType(CSharpDeclaration declaration) =>
        ControllerBase().IsMatch(declaration.BaseList) || HubBase().IsMatch(declaration.BaseList) || GrpcServiceBase().IsMatch(declaration.BaseList);

    private static bool IsType(PlacedDeclaration placed) => placed.Declaration.Kind == DeclarationKind.Type;

    /// <summary>
    /// Whether every type around a declaration is public: a member of an <c>internal</c> class is not reachable from outside the
    /// assembly. A type declared public in any of its parts (a partial class) counts as public; a container that is no type seen
    /// in the change (a namespace) does not hide anything.
    /// </summary>
    private static bool EnclosingTypesPublic(string container, HashSet<string> types, HashSet<string> publicTypes)
    {
        for (string path = container; path.Length > 0; path = path.LastIndexOf('.') is int dot and >= 0 ? path[..dot] : string.Empty)
        {
            if (types.Contains(path) && !publicTypes.Contains(path))
            {
                return false;
            }
        }

        return true;
    }

    private static ChangeSignal Signal(string id, ChangeClass raisesTo, string where, string detail) => new(id, raisesTo, SignalStrength.Heuristic, where, detail);

    [GeneratedRegex(@"^\[\s*(?:\w+\s*:\s*)?(?:[\w.]+\.)?(?:Http(?:Get|Post|Put|Delete|Patch|Head|Options)|Route|ApiController|AcceptVerbs)(?:Attribute)?\b|[\[,]\s*(?:\w+\s*:\s*)?(?:[\w.]+\.)?(?:Http(?:Get|Post|Put|Delete|Patch|Head|Options)|Route|ApiController|AcceptVerbs)(?:Attribute)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex RoutingAttribute();

    [GeneratedRegex(@"\.Map(?!From\b)\w*\s*[(<]|\.(?:RequireAuthorization|AllowAnonymous|RequireCors|RequireRateLimiting|RequireHost|DisableAntiforgery|WithMetadata)\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex MinimalEndpointCall();

    [GeneratedRegex(@"^\s*@(?:page|rendermode)\b|\[\s*Route\s*\(|@attribute\s*\[\s*Authorize", RegexOptions.CultureInvariant)]
    private static partial Regex PageDirectiveLine();

    [GeneratedRegex(@"(?:^|[\s,.:])\w*(?:ControllerBase|Controller)(?:$|[\s,<(])", RegexOptions.CultureInvariant)]
    private static partial Regex ControllerBase();

    [GeneratedRegex(@"^\[\s*(?:[\w.]+\.)?(?:ApiController|Controller|Route)(?:Attribute)?\b|[\[,]\s*(?:[\w.]+\.)?(?:ApiController|Controller|Route)(?:Attribute)?\b", RegexOptions.CultureInvariant)]
    private static partial Regex ControllerAttribute();

    [GeneratedRegex(@"(?:^|[\s,.:])Hub(?:$|[\s,<(])|(?:^|[\s,.:])Hub<", RegexOptions.CultureInvariant)]
    private static partial Regex HubBase();

    [GeneratedRegex(@"\w\.\w+Base(?:$|[\s,<(])", RegexOptions.CultureInvariant)]
    private static partial Regex GrpcServiceBase();
}
