using Lockflight.Cli.Gates.Suppressions;

namespace Lockflight.Cli.Tests.Gates.Suppressions;

public sealed class SuppressionPatternsTests
{
    public static TheoryData<string, string, string> Suppressing() => new()
    {
        { "src/A.cs", "#pragma warning disable CA1822", "CA1822 Pragma" },
        { "src/A.cs", "    #pragma warning disable CS0168, CS0219 // unused", "CS0168 Pragma,CS0219 Pragma" },
        { "src/A.cs", "#pragma warning disable", "all Pragma" },
        { "src/A.cs", "#nullable disable", "nullable NullableDisable" },
        { "src/A.cs", "#nullable disable warnings", "nullable NullableDisable" },
        { "src/A.cs", "[SuppressMessage(\"Performance\", \"CA1822:Mark members as static\", Justification = \"x\")]", "CA1822 SuppressMessage" },
        { "src/GlobalSuppressions.cs", "[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(\"Style\", \"IDE0005\")]", "IDE0005 SuppressMessage" },
        { "src/A.cs", "[UnconditionalSuppressMessage(\"Trimming\", \"IL2026:Requires unreferenced code\")]", "IL2026 SuppressMessage" },
        { "src/A.cs", "[ExcludeFromCodeCoverage]", "ExcludeFromCodeCoverage ExcludeFromCodeCoverage" },
        { "src/A.cs", "[Serializable, ExcludeFromCodeCoverage(Justification = \"generated\")]", "ExcludeFromCodeCoverage ExcludeFromCodeCoverage" },
        { "src/A.csproj", "    <NoWarn>$(NoWarn);CS1591;NU1605</NoWarn>", "CS1591 NoWarn,NU1605 NoWarn" },
        { "Directory.Build.props", "<WarningsNotAsErrors>CA2007</WarningsNotAsErrors>", "CA2007 WarningsNotAsErrors" },
        { "src/A.csproj", "<PackageReference Include=\"Old\" Version=\"1.0.0\" NoWarn=\"NU1701\" />", "NU1701 NoWarn" },
        { ".editorconfig", "dotnet_diagnostic.CA1822.severity = none", "CA1822 LoweredSeverity" },
        { "src/.editorconfig", "dotnet_analyzer_diagnostic.category-Design.severity = silent", "category-Design LoweredSeverity" },
        { "rules.globalconfig", "dotnet_analyzer_diagnostic.severity = suggestion", "all LoweredSeverity" },
    };

    public static TheoryData<string, string> Harmless() => new()
    {
        { "src/A.cs", "// #pragma warning disable CA1822" },
        { "src/A.cs", "var text = \"[SuppressMessage(\\\"x\\\", \\\"CA1\\\")]\";" },
        { "src/A.cs", "#pragma warning restore CA1822" },
        { "src/A.cs", "#nullable enable" },
        { "src/A.cs", "/* [ExcludeFromCodeCoverage] */ public void Run() { }" },
        { "src/A.csproj", "<NoWarn>$(NoWarn)</NoWarn>" },
        { "src/A.csproj", "<!-- <NoWarn>CS1591</NoWarn> -->" },
        { "README.md", "#pragma warning disable CA1822" },
    };

    [Theory]
    [MemberData(nameof(Suppressing))]
    public void ALineThatSilencesADiagnosticIsFoundWithItsRule(string path, string line, string expected) =>
        Assert.Equal(expected, Describe(path, line));

    [Theory]
    [MemberData(nameof(Harmless))]
    public void ALineThatOnlyMentionsASuppressionIsNotOne(string path, string line) =>
        Assert.Equal(string.Empty, Describe(path, line));

    [Fact]
    public void AnAnalyzerConfigLineCarriesItsSettingAndSeveritySoTheScanCanCompare()
    {
        SuppressionHit hit = Assert.Single(SuppressionPatterns.InLine(".editorconfig", "dotnet_diagnostic.CA2000.severity = warning", string.Empty));

        Assert.Equal("dotnet_diagnostic.ca2000.severity", hit.SettingKey);
        Assert.Equal(SuppressionPatterns.WarningSeverity, hit.Severity);
    }

    [Fact]
    public void ADirectiveInsideABlockCommentIsNotOne()
    {
        string[] masked = SuppressionPatterns.MaskedCSharpLines("/*\n#pragma warning disable CA1822\n*/\n#pragma warning disable CA2000\n");

        Assert.Empty(SuppressionPatterns.InLine("src/A.cs", "#pragma warning disable CA1822", masked[1]));
        Assert.Single(SuppressionPatterns.InLine("src/A.cs", "#pragma warning disable CA2000", masked[3]));
    }

    private static string Describe(string path, string line)
    {
        string masked = SuppressionPatterns.IsCSharp(path) ? SuppressionPatterns.MaskedCSharpLines(line)[0] : line;
        return string.Join(',', SuppressionPatterns.InLine(path, line, masked).Select(hit => $"{hit.Rule} {hit.Kind}"));
    }
}
