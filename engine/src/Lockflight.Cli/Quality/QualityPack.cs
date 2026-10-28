namespace Lockflight.Cli.Quality;

/// <summary>
/// The recommended code-quality setup <c>init --quality</c> offers: the recommended .NET analyzers (<c>AnalysisLevel</c> in
/// <c>Directory.Build.props</c>) and the code metrics rules CA1502, CA1505 and CA1506 as build warnings (<c>.editorconfig</c>),
/// with thresholds in <c>CodeMetricsConfig.txt</c>. Every addition is marked, so a file that has it is left alone.
/// </summary>
internal static class QualityPack
{
    public const string Marker = "Lockflight quality pack";
    public const string EditorConfigPath = ".editorconfig";
    public const string BuildPropsPath = "Directory.Build.props";
    public const string CodeMetricsPath = "CodeMetricsConfig.txt";

    private const string ProjectEnd = "</Project>";
    private const string AnalysisLevelElement = "<AnalysisLevel>";

    private const string EditorConfigSection = $"""
        # {Marker} (lockflight init --quality): the code metrics rules as build warnings. Their thresholds are in
        # CodeMetricsConfig.txt; AnalysisLevel in Directory.Build.props turns on the recommended .NET analyzers.
        [*.cs]
        dotnet_diagnostic.CA1502.severity = warning
        dotnet_diagnostic.CA1505.severity = warning
        dotnet_diagnostic.CA1506.severity = warning

        """;

    private const string AnalysisLevelGroup = """
          <PropertyGroup>
            <AnalysisLevel>latest-recommended</AnalysisLevel>
          </PropertyGroup>
        """;

    private const string MetricsGroup = $"""
          <ItemGroup>
            <AdditionalFiles Include="$(MSBuildThisFileDirectory){CodeMetricsPath}" />
          </ItemGroup>
        """;

    /// <summary>
    /// Thresholds: at most 15 linearly independent paths through a method (the rule's default, 25, lets very branchy methods
    /// through), a maintainability index of at least 10 (the rule's default), and the rule's default class coupling limits.
    /// </summary>
    private const string CodeMetrics = """
        CA1502(Method): 15
        CA1505: 10
        CA1506(Method): 40
        CA1506(Type): 95

        """;

    /// <summary>The pack's files that need writing or proposing; empty when every file already has its part.</summary>
    public static IReadOnlyList<QualityFile> Files(string root)
    {
        List<QualityFile> files = [];
        Add(files, root, EditorConfigPath, EditorConfig);
        Add(files, root, BuildPropsPath, BuildProps);
        Add(files, root, CodeMetricsPath, existing => existing is null ? CodeMetrics : null);
        return files;
    }

    /// <summary>The <c>.editorconfig</c> with the pack's section; null when it has it, or none can be added.</summary>
    public static string? EditorConfig(string? existing) => existing switch
    {
        null => "root = true\n\n" + EditorConfigSection,
        _ when existing.Contains(Marker, StringComparison.Ordinal) => null,
        _ => existing.TrimEnd('\n', '\r') + "\n\n" + EditorConfigSection,
    };

    /// <summary>
    /// <c>Directory.Build.props</c> with the analysis level and the metrics file, each unless it is already there; null when both
    /// are, or when the file has no closing <c>&lt;/Project&gt;</c> to add them before.
    /// </summary>
    public static string? BuildProps(string? existing)
    {
        if (existing is null)
        {
            return $"<Project>\n{Comment}\n{AnalysisLevelGroup}\n{MetricsGroup}\n</Project>\n";
        }

        string[] groups =
        [
            .. existing.Contains(AnalysisLevelElement, StringComparison.OrdinalIgnoreCase) ? Array.Empty<string>() : [AnalysisLevelGroup],
            .. existing.Contains(CodeMetricsPath, StringComparison.OrdinalIgnoreCase) ? Array.Empty<string>() : [MetricsGroup],
        ];
        int end = existing.LastIndexOf(ProjectEnd, StringComparison.Ordinal);
        return groups.Length == 0 || end < 0 ? null : existing[..end] + Comment + "\n" + string.Join('\n', groups) + "\n" + existing[end..];
    }

    /// <summary>The XML comment that marks the pack's part; an XML comment cannot hold a double hyphen, so no option is named.</summary>
    private static string Comment => $"  <!-- {Marker}, from lockflight init: the recommended .NET analyzers and the code metrics thresholds. -->";

    private static void Add(List<QualityFile> files, string root, string relativePath, Func<string?, string?> propose)
    {
        string path = Path.Combine(root, relativePath);
        string? existing = File.Exists(path) ? File.ReadAllText(path) : null;
        if (propose(existing) is string content)
        {
            files.Add(new QualityFile(relativePath, content, existing is not null));
        }
    }
}
