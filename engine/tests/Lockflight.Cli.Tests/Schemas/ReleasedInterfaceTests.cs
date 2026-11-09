using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lockflight.Cli.Hosting;
using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Tests.Schemas;

/// <summary>
/// The parts of the public contracts a release published as definitions rather than files it wrote: its JSON Schemas, the CI
/// action's inputs and outputs, the workflow init generated, and the exit codes. Against each released version, the current
/// ones only add.
/// </summary>
public sealed partial class ReleasedInterfaceTests
{
    private static readonly string CurrentAction = Path.Combine(SchemaFiles.RepositoryRoot, "action", "action.yml");

    public static TheoryData<string, string> ReleasedSchemas() =>
    [
        .. ReleasedFixtures.Versions.SelectMany(version => ReleasedFixtures.Files(version, "schemas").Select(path => (version, Path.GetFileName(path)))),
    ];

    [Theory]
    [MemberData(nameof(ReleasedSchemas))]
    public void EveryReleasedSchemaIsStillPublishedAndOnlyGrew(string version, string schema)
    {
        string current = Path.Combine(SchemaFiles.Directory, schema);
        Assert.True(File.Exists(current), $"schemas/{schema}, published in {version}, is gone");

        IReadOnlyList<string> breaks = SchemaAdditivity.Breaks(
            JsonNode.Parse(ReleasedFixtures.Read(version, $"schemas/{schema}"))!, JsonNode.Parse(File.ReadAllText(current))!);

        Assert.True(breaks.Count == 0, $"schemas/{schema} is not an addition to {version}'s: {string.Join("; ", breaks)}");
    }

    [Theory]
    [MemberData(nameof(ReleasedFixtures.All), MemberType = typeof(ReleasedFixtures))]
    public void EveryReleasedActionInputAndOutputIsKeptWithItsDefault(string version)
    {
        YamlMappingNode released = Load(ReleasedFixtures.PathOf(version, "action.yml"));
        YamlMappingNode current = Load(CurrentAction);

        foreach ((string name, YamlMappingNode input) in Entries(released, "inputs"))
        {
            YamlMappingNode? now = Entries(current, "inputs").Where(entry => entry.Name == name).Select(entry => entry.Value).SingleOrDefault();
            Assert.True(now is not null, $"the action's input '{name}' of {version} is gone");
            Assert.True(Scalar(now, "required") != "true" || Scalar(input, "required") == "true", $"the action's input '{name}' became required after {version}");
            Assert.True(Scalar(now, "default") == Scalar(input, "default"), $"the default of the action's input '{name}' changed after {version}");
        }

        string[] outputs = [.. Entries(current, "outputs").Select(entry => entry.Name)];
        Assert.All(Entries(released, "outputs"), output => Assert.Contains(output.Name, outputs));
    }

    [Theory]
    [MemberData(nameof(ReleasedFixtures.All), MemberType = typeof(ReleasedFixtures))]
    public void TheWorkflowInitGeneratedOnlyUsesInputsTheActionStillHas(string version)
    {
        string[] inputs = [.. Entries(Load(CurrentAction), "inputs").Select(entry => entry.Name)];
        YamlMappingNode workflow = Load(ReleasedFixtures.PathOf(version, "repository/.github/workflows/lockflight-gates.yml"));
        List<string> used = [];
        foreach (YamlMappingNode job in ((YamlMappingNode)workflow.Children[new YamlScalarNode("jobs")]).Children.Values.Cast<YamlMappingNode>())
        {
            foreach (YamlMappingNode step in ((YamlSequenceNode)job.Children[new YamlScalarNode("steps")]).Children.Cast<YamlMappingNode>())
            {
                if (Scalar(step, "uses") is string uses && uses.StartsWith("lockflight/lockflight/action@", StringComparison.Ordinal)
                    && step.Children.TryGetValue(new YamlScalarNode("with"), out YamlNode? with))
                {
                    used.AddRange(((YamlMappingNode)with).Children.Keys.Select(key => ((YamlScalarNode)key).Value!));
                }
            }
        }

        Assert.Contains("mode", used);
        Assert.All(used, input => Assert.Contains(input, inputs));
    }

    [Theory]
    [MemberData(nameof(ReleasedFixtures.All), MemberType = typeof(ReleasedFixtures))]
    public void EveryReleasedExitCodeKeepsItsMeaning(string version)
    {
        Dictionary<int, string> released = ExitCodes(ReleasedFixtures.Read(version, "exit-codes.md"));
        Dictionary<int, string> current = ExitCodes(File.ReadAllText(Path.Combine(SchemaFiles.RepositoryRoot, "docs", "guides", "commands.md")));

        Assert.NotEmpty(released);
        foreach ((int code, string meaning) in released)
        {
            Assert.True(Enum.IsDefined((ExitCode)code), $"exit code {code} of {version} is gone from the engine");
            Assert.True(current.TryGetValue(code, out string? now) && now == meaning, $"exit code {code} meant {meaning} in {version}, now {now ?? "nothing"}");
        }

        Assert.Equal(Enum.GetValues<ExitCode>().Select(code => (int)code).Order(), current.Keys.Order());
    }

    private static YamlMappingNode Load(string path)
    {
        YamlStream stream = new();
        using StreamReader reader = new(path);
        stream.Load(reader);
        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static IEnumerable<(string Name, YamlMappingNode Value)> Entries(YamlMappingNode root, string section) =>
        root.Children.TryGetValue(new YamlScalarNode(section), out YamlNode? node)
            ? ((YamlMappingNode)node).Children.Select(pair => (((YamlScalarNode)pair.Key).Value!, (YamlMappingNode)pair.Value))
            : [];

    private static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value) && value is YamlScalarNode scalar ? scalar.Value : null;

    /// <summary>The exit code table's rows, each code with the first word of its meaning (PASS, FAIL, blocked, ERROR, …).</summary>
    private static Dictionary<int, string> ExitCodes(string markdown)
    {
        int start = markdown.IndexOf("## Exit codes", StringComparison.Ordinal);
        string table = start < 0 ? markdown : markdown[start..];
        int end = table.IndexOf("\n## ", 1, StringComparison.Ordinal);
        return ExitCodeRow().Matches(end < 0 ? table : table[..end])
            .ToDictionary(match => int.Parse(match.Groups["code"].Value, CultureInfo.InvariantCulture), match => match.Groups["meaning"].Value);
    }

    [GeneratedRegex(@"^\| (?<code>[0-9]) \| (?<meaning>[A-Za-z-]+)", RegexOptions.Multiline)]
    private static partial Regex ExitCodeRow();
}
