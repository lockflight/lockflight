using Lockflight.Cli.Ci;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// A snapshot store's record mode in the host-tampering scan (<see cref="SnapshotRecords"/>), on samples compiled into assemblies
/// of their own, in Debug and optimized builds: the store that writes literal paths only behind an off-by-default snapshot
/// variable is no finding; every other shape of it is.
/// </summary>
public sealed class SnapshotRecordTests : IDisposable
{
    private const string Store = """
        using System;
        using System.IO;

        public static class Store
        {
            public static void Save(string text)
            {
                if (Environment.GetEnvironmentVariable("SHOP_SNAPSHOTS") == "record")
                {
                    Directory.CreateDirectory(Path.Combine("Snapshots", "orders"));
                    File.WriteAllText(Path.Combine("Snapshots", "orders", "total.verified.json"), text);
                }
            }

            public static void SaveIgnoringCase(string text)
            {
                if (string.Equals(Environment.GetEnvironmentVariable("SHOP_SNAPSHOTS"), "record", StringComparison.OrdinalIgnoreCase))
                {
                    File.WriteAllText("Snapshots/" + "orders/" + "lines.verified.txt", text);
                }
            }
        }
        """;

    private static readonly Dictionary<string, string> StillReported = new(StringComparer.Ordinal)
    {
        ["WithoutAGuard"] = Method("File.WriteAllText(\"Snapshots/a.verified.txt\", text);"),
        ["AfterTheGuard"] = Method("if (Read() == \"record\") { Directory.CreateDirectory(\"Snapshots\"); } File.WriteAllText(\"Snapshots/a.verified.txt\", text);", "static string Read() => Environment.GetEnvironmentVariable(\"SHOP_SNAPSHOTS\");"),
        ["UnlessSwitchedOff"] = Method("if (Environment.GetEnvironmentVariable(\"SHOP_SNAPSHOTS\") != \"off\") { File.WriteAllText(\"Snapshots/a.verified.txt\", text); }"),
        ["OnAVariableTheRunnerSets"] = Method("if (Environment.GetEnvironmentVariable(\"CONTINUOUS_INTEGRATION\") == \"true\") { File.WriteAllText(\"Snapshots/a.verified.txt\", text); }"),
        ["OnTheGuardOrAFlag"] = Method("if (Environment.GetEnvironmentVariable(\"SHOP_SNAPSHOTS\") == \"record\" || text.Length > 0) { File.WriteAllText(\"Snapshots/a.verified.txt\", text); }"),
        ["OnAFlagOrTheGuard"] = Method("if (text.Length > 0 || Environment.GetEnvironmentVariable(\"SHOP_SNAPSHOTS\") == \"record\") { File.WriteAllText(\"Snapshots/a.verified.txt\", text); }"),
        ["OnAnotherLocal"] = Method("bool go = text.Length > 0; bool record = Environment.GetEnvironmentVariable(\"SHOP_SNAPSHOTS\") == \"record\"; if (go) { File.WriteAllText(\"Snapshots/a.verified.txt\", text); } GC.KeepAlive(record);"),
        ["TheProjectFile"] = Guarded("File.WriteAllText(\"Shop.Tests.csproj\", text);"),
        ["ASource"] = Guarded("File.WriteAllText(\"Snapshots/a.verified.cs\", text);"),
        ["TheRunnerConfig"] = Guarded("File.WriteAllText(\"xunit.runner.json\", text);"),
        ["TheBuildProps"] = Guarded("File.WriteAllText(\"Directory.Build.props\", text);"),
        ["AboveTheFolder"] = Guarded("File.WriteAllText(Path.Combine(\"..\", \"a.verified.txt\"), text);"),
        ["ARootedPath"] = Guarded("File.WriteAllText(\"/tmp/a.verified.txt\", text);"),
        ["ADelete"] = Guarded("File.Delete(\"Snapshots/a.verified.txt\");"),
        ["BesideTheBuildOutput"] = Guarded("File.WriteAllText(Path.Combine(AppContext.BaseDirectory, \"a.verified.txt\"), text);"),
        ["AFileAFieldNames"] = Guarded("File.WriteAllText(Config, text);", "static readonly string Config = \"xunit.runner.json\";"),
        ["AFolderFromAParameter"] = Method("if (Environment.GetEnvironmentVariable(\"SHOP_SNAPSHOTS\") == \"record\") { File.WriteAllText(Path.Combine(directory, \"a.verified.txt\"), text); }", parameters: "string directory, string text"),
        ["ANameFromAParameter"] = Method("if (Environment.GetEnvironmentVariable(\"SHOP_SNAPSHOTS\") == \"record\") { File.WriteAllText(Path.Combine(\"Snapshots\", name + \".verified.txt\"), text); }", parameters: "string name, string text"),
        ["TheCallersSourceFile"] = Method("if (Environment.GetEnvironmentVariable(\"SHOP_SNAPSHOTS\") == \"record\") { File.WriteAllText(source, text); }", parameters: "string text, [System.Runtime.CompilerServices.CallerFilePath] string source = \"\""),
        ["APathAnInterfaceGives"] = Method("if (Environment.GetEnvironmentVariable(\"SHOP_SNAPSHOTS\") == \"record\") { File.WriteAllText(paths.Target(), text); }", "public interface IPaths { string Target(); }", "IPaths paths, string text"),
        ["APathADelegateGives"] = Method("if (Environment.GetEnvironmentVariable(\"SHOP_SNAPSHOTS\") == \"record\") { File.WriteAllText(target(), text); }", parameters: "Func<string> target, string text"),
        ["AnAwaitInsideTheGuard"] = """
            using System; using System.IO; using System.Threading.Tasks;
            public static class Sample
            {
                public static async Task Write(string text)
                {
                    if (Environment.GetEnvironmentVariable("SHOP_SNAPSHOTS") == "record") { await File.WriteAllTextAsync("Snapshots/a.verified.txt", text); }
                }
            }
            """,
        ["TheAssemblySetsTheVariable"] = Guarded("File.WriteAllText(\"Snapshots/a.verified.txt\", text);", "public static void On() => Environment.SetEnvironmentVariable(\"SHOP_SNAPSHOTS\", \"record\");"),
        ["TheAssemblySetsTheVariableThroughAField"] = Guarded("File.WriteAllText(\"Snapshots/a.verified.txt\", text);", "static readonly string Name = \"SHOP_SNAPSHOTS\"; public static void On() => Environment.SetEnvironmentVariable(Name, \"record\");"),
        ["TheAssemblySetsTheVariableThroughAHelper"] = Guarded("File.WriteAllText(\"Snapshots/a.verified.txt\", text);", "public static void On() => Set(\"SHOP_SNAPSHOTS\", \"record\"); static void Set(string name, string value) => Environment.SetEnvironmentVariable(name, value);"),
        ["TheAssemblySetsAVariableBuiltAtRunTime"] = Guarded("File.WriteAllText(\"Snapshots/a.verified.txt\", text);", "public static void On() => Environment.SetEnvironmentVariable(string.Join(\"_\", \"SHOP\", \"SNAPSHOTS\"), \"record\");"),
        ["TheAssemblyCallsSetenv"] = Guarded("File.WriteAllText(\"Snapshots/a.verified.txt\", text);", "[System.Runtime.InteropServices.DllImport(\"libc\")] static extern int setenv(string name, string value, int overwrite); public static void On() => setenv(\"SHOP_SNAPSHOTS\", \"record\", 1);"),
    };

    private readonly SnapshotSampleCompiler compiler = new();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AStoreThatWritesLiteralPathsOnlyInRecordModeIsNoFinding(bool optimize) =>
        Assert.DoesNotContain(HostTamperingScanner.ScanAll([compiler.Compile("Shop.Tests", Store, optimize)], [], NamedNowhere), finding => finding.Kind == "file-write");

    public static TheoryData<string, bool> Cases() => [.. StillReported.Keys.SelectMany(name => new[] { (name, false), (name, true) })];

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryOtherShapeOfARecordModeWriteIsAFinding(string name, bool optimize) =>
        Assert.Contains(HostTamperingScanner.ScanAll([compiler.Compile(name, StillReported[name], optimize)], [], NamedNowhere), finding => finding.Kind == "file-write");

    [Fact]
    public void AVariableBuiltAtRunTimeIsReportedAsSwitchingTheRecordModeOn()
    {
        string sample = compiler.Compile("Shop.Tests", Store.Replace("public static class Store", "public static class Store\n{\n    public static void On() => Environment.SetEnvironmentVariable(string.Join(\"_\", \"SHOP\", \"SNAPSHOTS\"), \"record\");\n}\n\npublic static class Other", StringComparison.Ordinal));

        IReadOnlyList<TamperingFinding> findings = HostTamperingScanner.ScanAll([sample], [], NamedNowhere);

        Assert.Contains(findings, finding => finding.Kind == "file-write" && finding.Location == "Other::Save");
        TamperingFinding switched = Assert.Single(findings, finding => finding.Kind == "snapshot-switch");
        Assert.Equal("Store::On", switched.Location);
    }

    [Fact]
    public void AHelperAssemblyThatSetsTheVariableAllowsNoRecordModeWrite()
    {
        string kit = compiler.Compile("Shop.TestKit", "public static class Env { static Env() { System.Environment.SetEnvironmentVariable(\"SHOP_SNAPSHOTS\", \"record\"); } public static void Touch() { } }");
        string tests = compiler.Compile("Shop.Tests", Store);

        Assert.Contains(HostTamperingScanner.ScanAll([tests, kit], [], NamedNowhere), finding => finding.Kind == "file-write" && finding.Assembly == "Shop.Tests");
    }

    [Fact]
    public void AProductionAssemblyThatSetsAVariableAtRunTimeAllowsNoRecordModeWrite()
    {
        string production = compiler.Compile("Shop", "public static class Startup { public static void Configure(string name) => System.Environment.SetEnvironmentVariable(name, \"1\"); }");
        string tests = compiler.Compile("Shop.Tests", Store);

        Assert.Contains(HostTamperingScanner.ScanAll([tests], [production], NamedNowhere), finding => finding.Kind == "file-write" && finding.Assembly == "Shop.Tests");
    }

    [Fact]
    public void AVariableNamedOutsideTheTestCodeAllowsNoRecordModeWrite() =>
        Assert.Contains(
            HostTamperingScanner.ScanAll([compiler.Compile("Shop.Tests", Store)], [], (name, assembly) => assembly == "Shop.Tests" && name.EndsWith("_SNAPSHOTS", StringComparison.Ordinal)),
            finding => finding.Kind == "file-write");

    public void Dispose() => compiler.Dispose();

    private static bool NamedNowhere(string name, string assembly) => false;

    private static string Guarded(string write, string members = "") =>
        Method($"if (Environment.GetEnvironmentVariable(\"SHOP_SNAPSHOTS\") == \"record\") {{ {write} }}", members);

    private static string Method(string body, string members = "", string parameters = "string text") => $$"""
        using System;
        using System.IO;

        {{(members.StartsWith("public interface", StringComparison.Ordinal) ? members : string.Empty)}}

        public static class Sample
        {
            {{(members.StartsWith("public interface", StringComparison.Ordinal) ? string.Empty : members)}}

            public static void Write({{parameters}})
            {
                {{body}}
            }
        }
        """;
}
