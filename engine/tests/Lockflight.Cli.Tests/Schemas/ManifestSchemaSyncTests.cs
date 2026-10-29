using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;
using Lockflight.Cli.Config;

namespace Lockflight.Cli.Tests.Schemas;

/// <summary>
/// The engine's manifest reader is the authority; the published schema mirrors it. Every fixture must get the same verdict from
/// both, except the <c>engine-only</c> ones: rules a JSON Schema cannot express, which the schema's descriptions name.
/// </summary>
public sealed partial class ManifestSchemaSyncTests
{
    private const string Id = "lockflight.manifest.v2";
    private const string UpdateHint = "update schemas/lockflight.manifest.v2.json and copy it to lockflight.schema.json";

    private static readonly JsonSchema Schema = JsonSchema.FromFile(Path.Combine(SchemaFiles.Directory, $"{Id}.json"));

    public static TheoryData<string> Valid() => [.. SchemaFiles.Fixtures(Id, "valid").Select(path => Path.GetFileName(path))];

    public static TheoryData<string> Invalid() => [.. SchemaFiles.Fixtures(Id, "invalid").Select(path => Path.GetFileName(path))];

    public static TheoryData<string> EngineOnly() => [.. SchemaFiles.Fixtures(Id, "engine-only").Select(path => Path.GetFileName(path))];

    [Theory]
    [MemberData(nameof(Valid))]
    public void AValidFixtureIsAcceptedByTheEngineAndTheSchema(string fixture)
    {
        string yaml = Read("valid", fixture);

        Assert.Equal(ManifestLoadStatus.Loaded, Engine(yaml).Status);
        Assert.True(SchemaAccepts(yaml), $"the schema refuses {fixture}; {UpdateHint}");
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void AnInvalidFixtureIsRefusedByTheEngineAndTheSchema(string fixture)
    {
        string yaml = Read("invalid", fixture);

        Assert.Equal(ManifestLoadStatus.Invalid, Engine(yaml).Status);
        Assert.False(SchemaAccepts(yaml), $"the schema accepts {fixture}; {UpdateHint}");
    }

    [Theory]
    [MemberData(nameof(EngineOnly))]
    public void ARuleTheSchemaCannotExpressIsRefusedByTheEngineOnly(string fixture)
    {
        string yaml = Read("engine-only", fixture);

        Assert.Equal(ManifestLoadStatus.Invalid, Engine(yaml).Status);
        Assert.True(SchemaAccepts(yaml), $"the schema now refuses {fixture}: move it to invalid/");
    }

    [Fact]
    public void TheRootSchemaIsACopyOfTheNewestManifestSchema() =>
        Assert.Equal(
            File.ReadAllText(Path.Combine(SchemaFiles.Directory, $"{Id}.json")).ReplaceLineEndings("\n"),
            File.ReadAllText(Path.Combine(SchemaFiles.RepositoryRoot, "lockflight.schema.json")).ReplaceLineEndings("\n"));

    [Theory]
    [InlineData("", "version: 2\nx: 1\n")]
    [InlineData("gates", "version: 2\ngates:\n  x: 1\n")]
    [InlineData("gates.test", "version: 2\ngates:\n  test:\n    x: 1\n")]
    [InlineData("gates.coverage", "version: 2\ngates:\n  coverage:\n    x: 1\n")]
    [InlineData("gates.quality", "version: 2\ngates:\n  quality:\n    x: 1\n")]
    [InlineData("gates.docs", "version: 2\ngates:\n  docs:\n    x: 1\n")]
    [InlineData("stop", "version: 2\nstop:\n  x: 1\n")]
    [InlineData("routing", "version: 2\nrouting:\n  preset: default\n  x: 1\n")]
    [InlineData("routing.fix", "version: 2\nrouting:\n  preset: default\n  fix:\n    x: 1\n")]
    [InlineData("routing.coverage", "version: 2\nrouting:\n  preset: default\n  coverage:\n    x: 1\n")]
    [InlineData("routing.require", "version: 2\nrouting:\n  preset: default\n  require:\n    x: 1\n")]
    [InlineData("approvals", "version: 2\napprovals:\n  x: 1\n")]
    [InlineData("autonomy", "version: 2\nautonomy:\n  x: 1\n")]
    [InlineData("units", "version: 2\nunits:\n  - path: a\n    x: 1\n")]
    [InlineData("units.test", "version: 2\nunits:\n  - path: a\n    test:\n      x: 1\n")]
    [InlineData("shell", "version: 2\nshell:\n  x: 1\n")]
    [InlineData("review", "version: 2\nreview:\n  x: 1\n")]
    public void EverySectionKnowsTheSameKeysInTheEngineAndTheSchema(string section, string yaml)
    {
        string message = Engine(yaml).Error!.Message;
        string[] engineKeys = [.. KnownList().Match(message).Groups["keys"].Value.Split(", ").Order(StringComparer.Ordinal)];
        JsonNode schemaSection = section.Split('.', StringSplitOptions.RemoveEmptyEntries).Aggregate(
            JsonNode.Parse(File.ReadAllText(Path.Combine(SchemaFiles.Directory, $"{Id}.json")))!,
            (node, key) => key == "units" ? node["properties"]![key]!["items"]! : node["properties"]![key]!);
        string[] schemaKeys = [.. schemaSection["properties"]!.AsObject().Select(property => property.Key).Order(StringComparer.Ordinal)];

        Assert.True(engineKeys.SequenceEqual(schemaKeys), $"'{section}': engine knows [{string.Join(", ", engineKeys)}], schema [{string.Join(", ", schemaKeys)}]; {UpdateHint}");
    }

    private static string Read(string kind, string fixture) => File.ReadAllText(Path.Combine(SchemaFiles.Directory, "fixtures", Id, kind, fixture));

    private static ManifestLoadResult Engine(string yaml) => new ManifestParser("/repo", "/repo/lockflight.yaml").Parse(yaml);

    private static bool SchemaAccepts(string yaml)
    {
        JsonElement document = JsonSerializer.SerializeToElement(YamlJson.Convert(yaml));
        return Schema.Evaluate(document).IsValid;
    }

    [GeneratedRegex(@"\(known: (?<keys>[^)]*)\)")]
    private static partial Regex KnownList();
}
