namespace Lockflight.Cli.Hosting;

/// <summary>Reads environment variables: the process environment, or a fixed set so tests do not depend on the shell they run in.</summary>
internal sealed class EnvironmentReader(IReadOnlyDictionary<string, string>? fixedVariables)
{
    public static EnvironmentReader Process { get; } = new(null);

    public static EnvironmentReader Of(IReadOnlyDictionary<string, string> variables) => new(variables);

    /// <summary>The variable's value; null when unset.</summary>
    public string? Get(string name) =>
        fixedVariables is null ? Environment.GetEnvironmentVariable(name) : fixedVariables.GetValueOrDefault(name);

    /// <summary>The user's home directory (<c>HOME</c>, else <c>USERPROFILE</c>); null when neither is set.</summary>
    public string? HomeDirectory() => NonEmpty(Get(EnvironmentVariables.Home)) ?? NonEmpty(Get(EnvironmentVariables.UserProfile));

    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
