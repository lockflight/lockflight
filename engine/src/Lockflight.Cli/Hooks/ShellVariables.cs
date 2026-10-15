using System.Text.RegularExpressions;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Resolves the variables the guard can know in a write target: <c>$PWD</c> (the session's working directory) and <c>$HOME</c>,
/// in POSIX (<c>$NAME</c>, <c>${NAME}</c>) and PowerShell (<c>$env:NAME</c>, <c>${env:NAME}</c>) spellings.
/// </summary>
internal static partial class ShellVariables
{
    public static ShellVariableResolution Resolve(string target, string workingDirectory, string? home)
    {
        if (!target.Contains('$', StringComparison.Ordinal))
        {
            return new ShellVariableResolution(target, null);
        }

        int unknownEnd = -1;
        string resolved = Reference().Replace(target, match =>
        {
            string name = match.Value.TrimStart('$').Trim('{', '}');
            name = name.StartsWith("env:", StringComparison.OrdinalIgnoreCase) ? name[4..] : name;
            string? value = name.ToUpperInvariant() switch
            {
                "PWD" => workingDirectory,
                "HOME" or "USERPROFILE" => home,
                _ => null,
            };
            if (value is null)
            {
                unknownEnd = match.Index + match.Length;
            }

            return value ?? match.Value;
        });

        return unknownEnd < 0
            ? new ShellVariableResolution(resolved, null)
            : new ShellVariableResolution(null, target[unknownEnd..].TrimStart('/', '\\').Replace('\\', '/'));
    }

    [GeneratedRegex(@"\$(\{[^}]*\}|(env:)?[A-Za-z_][A-Za-z0-9_]*|[0-9@*#?$!-])")]
    private static partial Regex Reference();
}
