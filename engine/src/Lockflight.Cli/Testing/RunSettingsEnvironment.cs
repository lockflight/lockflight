using System.Xml;
using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Testing;

/// <summary>
/// The environment variables a project's <c>.runsettings</c> sets for its tests (<c>RunConfiguration/EnvironmentVariables</c>), read
/// so the engine's own test runs set them too. <c>dotnet test</c> applies them on VSTest only: on Microsoft.Testing.Platform (the
/// .NET 10 SDK's <c>dotnet test</c> with <c>test.runner</c> in <c>global.json</c>) it does not pass <c>RunSettingsFilePath</c> to the
/// test application, so there they are not applied (<see cref="AppliesTo"/>): the engine's run matches <c>dotnet test</c>, not the file.
/// </summary>
internal static class RunSettingsEnvironment
{
    public static IReadOnlyDictionary<string, string?> None { get; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>Whether <c>dotnet test</c> on <paramref name="platform"/> applies the variables of the project's run settings.</summary>
    public static bool AppliesTo(TestRunnerKind platform) => platform == TestRunnerKind.VSTest;

    /// <summary>The variables, in file order; empty when the file is missing, unreadable or sets none.</summary>
    public static IReadOnlyDictionary<string, string?> Read(string path)
    {
        try
        {
            using XmlReader reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            XmlDocument document = new() { XmlResolver = null };
            document.Load(reader);
            Dictionary<string, string?> variables = new(StringComparer.Ordinal);
            if (document.SelectSingleNode("/RunSettings/RunConfiguration/EnvironmentVariables") is XmlElement section)
            {
                foreach (XmlElement variable in section.ChildNodes.OfType<XmlElement>())
                {
                    variables[variable.Name] = variable.InnerText;
                }
            }

            return variables;
        }
        catch (Exception exception) when (exception is XmlException or IOException or UnauthorizedAccessException)
        {
            return None;
        }
    }
}
