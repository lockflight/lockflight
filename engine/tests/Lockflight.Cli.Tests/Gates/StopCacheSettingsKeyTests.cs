using System.Reflection;
using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Tests.Gates;

/// <summary>
/// The Stop cache reuses a PASS only while every setting the gates run with is unchanged: one case per field of the stop settings,
/// <c>gates:</c> (coverage included) and each unit.
/// </summary>
public sealed class StopCacheSettingsKeyTests : IDisposable
{
    private const string Base = """
        version: 1
        stop:
          gate: true
          target: services
        gates:
          timeoutSeconds: 300
          build: App.sln
          test:
            framework: net8.0
          coverage:
            collector: coverlet
            patchThreshold: 70
            minChangedLines: 5
          quality:
            suppressions: fail
          docs:
            references: fail
        units:
          - path: services/orders
            build: Orders.sln
            kind: dotnet
            test:
              framework: net8.0
              stopFilter: Category!=Integration
        """;

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-settings-key-");

    public static TheoryData<string, string, string> Changes() => new()
    {
        { "stop.gate", "  gate: true", "  gate: false" },
        { "stop.target", "  target: services", "  target: services/orders" },
        { "gates.timeoutSeconds", "  timeoutSeconds: 300", "  timeoutSeconds: 301" },
        { "gates.build", "  build: App.sln", "  build: Other.sln" },
        { "gates.test.framework", "    framework: net8.0\n  coverage:", "    framework: net9.0\n  coverage:" },
        { "gates.coverage.collector", "    collector: coverlet", "    collector: microsoft" },
        { "gates.coverage.patchThreshold", "    patchThreshold: 70", "    patchThreshold: 71" },
        { "gates.coverage.minChangedLines", "    minChangedLines: 5", "    minChangedLines: 6" },
        { "gates.quality.suppressions", "    suppressions: fail", "    suppressions: report" },
        { "gates.docs.references", "    references: fail", "    references: off" },
        { "units[].path", "  - path: services/orders", "  - path: services/billing" },
        { "units[].build", "    build: Orders.sln", "    build: Orders.slnx" },
        { "units[].kind", "    kind: dotnet", "    kind: none" },
        { "units[].test.framework", "      framework: net8.0", "      framework: net9.0" },
        { "units[].test.stopFilter", "      stopFilter: Category!=Integration", "      stopFilter: Category!=Slow" },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void ChangingOneSettingChangesTheKey(string setting, string before, string after)
    {
        string baseKey = KeyFor(Base);
        Assert.Contains(before, Base, StringComparison.Ordinal);

        string changedKey = KeyFor(Base.Replace(before, after, StringComparison.Ordinal));

        Assert.True(baseKey != changedKey, $"changing {setting} keeps the key");
    }

    [Fact]
    public void AnUnchangedManifestKeepsTheKey() => Assert.Equal(KeyFor(Base), KeyFor(Base));

    /// <summary>A field added to one of these records must join the key, and a case above, before this list is updated.</summary>
    [Theory]
    [InlineData(typeof(GateSettings), "BuildFile,Coverage,DocsReferences,Suppressions,TestFramework,TimeoutSeconds")]
    [InlineData(typeof(CoverageSettings), "Collector,MinChangedLines,PatchThreshold")]
    [InlineData(typeof(Unit), "Build,Kind,Path,StopFilter,TestFramework")]
    public void EverySettingsFieldIsKnownToTheKey(Type settings, string fields) =>
        Assert.Equal(fields, string.Join(',', settings.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(property => property.Name).Order(StringComparer.Ordinal)));

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private string KeyFor(string manifest)
    {
        File.WriteAllText(Path.Combine(root.FullName, Manifest.FileName), manifest);
        Assert.NotNull(ManifestLocator.Discover(root.FullName).Manifest);
        return InputsHash.EngineAndSettings(root.FullName);
    }
}
