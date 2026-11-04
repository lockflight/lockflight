using System.Collections;

namespace Lockflight.Cli.Testing;

/// <summary>
/// The environment variables that commonly change how .NET code behaves (<c>DOTNET_*</c>, <c>ASPNETCORE_*</c>, <c>TZ</c>, <c>LANG</c>,
/// <c>LC_*</c>), for a message that compares two test runs.
/// </summary>
internal static class RunnerEnvironment
{
    private const string Redacted = "<redacted>";

    private static readonly string[] Prefixes = ["DOTNET_", "ASPNETCORE_", "LC_"];

    private static readonly string[] Names = ["TZ", "LANG"];

    /// <summary>Parts of a name whose value is shown as <see cref="Redacted"/>: a connection string, password or key does not go into the output.</summary>
    private static readonly string[] SecretNameParts = ["PASSWORD", "PWD", "SECRET", "TOKEN", "KEY", "CONNECTIONSTRING", "CREDENTIAL"];

    /// <summary>Whether the variable is one of those that commonly change how .NET code behaves.</summary>
    public static bool IsRelevant(string name) =>
        Names.Contains(name, StringComparer.Ordinal) || Prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    /// <summary>
    /// The relevant variables a process gets from <paramref name="inherited"/> with <paramref name="overrides"/> applied (a null value
    /// removes one), as <c>NAME=value</c> in name order, the value of one whose name looks like a secret redacted; "none" when there are none.
    /// </summary>
    public static string Describe(IReadOnlyDictionary<string, string?> inherited, params IReadOnlyList<IReadOnlyDictionary<string, string?>> overrides)
    {
        Dictionary<string, string?> variables = new(inherited, StringComparer.Ordinal);
        foreach (IReadOnlyDictionary<string, string?> layer in overrides)
        {
            foreach ((string name, string? value) in layer)
            {
                variables[name] = value;
            }
        }

        string[] relevant = [.. variables.Where(variable => variable.Value is not null && IsRelevant(variable.Key))
            .OrderBy(variable => variable.Key, StringComparer.Ordinal)
            .Select(variable => $"{variable.Key}={(LooksSecret(variable.Key) ? Redacted : variable.Value)}")];
        return relevant.Length == 0 ? "none" : string.Join(' ', relevant);
    }

    private static bool LooksSecret(string name) =>
        SecretNameParts.Any(part => name.Replace("_", string.Empty, StringComparison.Ordinal).Contains(part, StringComparison.OrdinalIgnoreCase));

    /// <summary>The engine's own environment, which every process it starts inherits.</summary>
    public static IReadOnlyDictionary<string, string?> Inherited() =>
        Environment.GetEnvironmentVariables().Cast<DictionaryEntry>()
            .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value, StringComparer.Ordinal);
}
