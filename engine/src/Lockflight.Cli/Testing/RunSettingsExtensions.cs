using System.Xml;

namespace Lockflight.Cli.Testing;

/// <summary>
/// What a <c>.runsettings</c> file loads into a VSTest run besides the tests: data collectors (out of process and in process), test
/// adapter paths and loggers. Each can rewrite or drop test outcomes without any code in the test assembly. The code-coverage
/// collectors (<c>Code Coverage</c>, <c>XPlat Code Coverage</c>) named without an assembly or code base are left out: they are
/// the SDK's and coverlet's own, which a coverage setup names.
/// </summary>
internal static class RunSettingsExtensions
{
    /// <summary>The kind of a data collector entry.</summary>
    public const string DataCollector = "runsettings-data-collector";

    /// <summary>The kind of an in-process data collector entry.</summary>
    public const string InProcessCollector = "runsettings-in-process-collector";

    /// <summary>The kind of a <c>RunConfiguration/TestAdaptersPaths</c> entry.</summary>
    public const string AdapterPath = "runsettings-adapter-path";

    /// <summary>The kind of a logger entry.</summary>
    public const string Logger = "runsettings-logger";

    /// <summary>The kind for a file that cannot be read as run settings.</summary>
    public const string Unreadable = "runsettings-unreadable";

    private static readonly string[] CoverageCollectors = ["Code Coverage", "XPlat Code Coverage"];

    private static readonly string[] IdentityAttributes = ["friendlyName", "uri", "assemblyQualifiedName", "codebase"];

    /// <summary>Each extension the file names, as (kind, identity); the identity joins the entry's name, URI, type and code base.</summary>
    public static IReadOnlyList<(string Kind, string Identity)> Read(string path)
    {
        XmlDocument document = new() { XmlResolver = null };
        try
        {
            using XmlReader reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document.Load(reader);
        }
        catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException)
        {
            return [(Unreadable, exception.GetType().Name)];
        }

        List<(string Kind, string Identity)> found = [];
        found.AddRange(Entries(document, "/RunSettings/DataCollectionRunSettings/DataCollectors/DataCollector")
            .Where(collector => !IsCoverage(collector))
            .Select(collector => (DataCollector, Identity(collector))));
        found.AddRange(Entries(document, "/RunSettings/InProcDataCollectionRunSettings/InProcDataCollectors/InProcDataCollector")
            .Select(collector => (InProcessCollector, Identity(collector))));
        found.AddRange(Entries(document, "/RunSettings/LoggerRunSettings/Loggers/Logger").Select(logger => (Logger, Identity(logger))));
        found.AddRange(Entries(document, "/RunSettings/RunConfiguration/TestAdaptersPaths")
            .SelectMany(paths => paths.InnerText.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(adapterPath => (AdapterPath, adapterPath)));
        return found;
    }

    private static IEnumerable<XmlElement> Entries(XmlDocument document, string xpath) =>
        document.SelectNodes(xpath)?.OfType<XmlElement>() ?? [];

    private static bool IsCoverage(XmlElement collector) =>
        CoverageCollectors.Contains(collector.GetAttribute("friendlyName"), StringComparer.OrdinalIgnoreCase)
        && collector.GetAttribute("assemblyQualifiedName").Length == 0
        && collector.GetAttribute("codebase").Length == 0;

    private static string Identity(XmlElement entry)
    {
        string[] parts = [.. IdentityAttributes.Select(entry.GetAttribute).Where(value => value.Length > 0)];
        return parts.Length > 0 ? string.Join(" ", parts) : entry.OuterXml;
    }
}
