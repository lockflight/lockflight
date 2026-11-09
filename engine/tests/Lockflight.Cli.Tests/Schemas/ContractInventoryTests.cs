using Lockflight.Cli.Adoption;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Docs;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Schemas;

/// <summary>The public contracts page names every published schema and every name the engine defines as a contract.</summary>
public sealed class ContractInventoryTests
{
    private static readonly string Page = File.ReadAllText(Path.Combine(SchemaFiles.RepositoryRoot, "docs", "guides", "contracts.md"));

    [Fact]
    public void EveryPublishedSchemaIsInTheInventory() =>
        Assert.All(Directory.EnumerateFiles(SchemaFiles.Directory, "*.json"), schema => Assert.Contains($"(../../schemas/{Path.GetFileName(schema)})", Page, StringComparison.Ordinal));

    [Fact]
    public void EveryMarkerContextAndExitCodeIsInTheInventory()
    {
        string[] names =
        [
            CommentRenderer.Marker, ClaudeMdBlock.Begin, ClaudeMdBlock.End, MarkdownDocument.IgnoreMarker,
            .. Enum.GetValues<StatusContext>().Select(context => $"`{StatusContextNames.ToName(context)}`"),
            .. Enum.GetValues<ExitCode>().Select(code => $"`{(int)code}`"),
            "`Lockflight-Red: <cycle>`",
        ];

        Assert.All(names, name => Assert.Contains(name, Page, StringComparison.Ordinal));
    }
}
