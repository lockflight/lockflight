namespace Lockflight.Cli.Gates.Parsers;

/// <summary>A package version with a published advisory, as reported by <c>dotnet package list --vulnerable</c>.</summary>
internal sealed record VulnerablePackage(string Id, string Version, string Severity, string AdvisoryUrl)
{
    public string Fingerprint => $"{Id}@{Version}|{AdvisoryUrl}";

    /// <summary>High and Critical advisories fail the gate; Moderate and Low are reported only.</summary>
    public bool IsBlocking => Severity is "High" or "Critical";

    public string Describe() => $"{Id} {Version} ({Severity}, {AdvisoryUrl})";
}
