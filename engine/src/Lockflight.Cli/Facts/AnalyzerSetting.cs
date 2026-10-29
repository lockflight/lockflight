namespace Lockflight.Cli.Facts;

/// <summary>A <c>dotnet_diagnostic.&lt;id&gt;.severity</c> line in a tracked analyzer config file.</summary>
/// <param name="Id">The diagnostic id, upper case (<c>CA1502</c>).</param>
/// <param name="Severity">The value as written (<c>warning</c>, <c>error</c>, <c>none</c>, …), lower case.</param>
/// <param name="File">The config file, relative to the repository root.</param>
internal sealed record AnalyzerSetting(string Id, string Severity, string File)
{
    /// <summary>Whether the build reports the diagnostic as a warning or an error, so the build gate sees it.</summary>
    public bool ReachesTheBuild => Severity is "warning" or "error";
}
