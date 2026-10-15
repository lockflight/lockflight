namespace Lockflight.Cli.Hooks;

/// <summary>
/// A cmdlet's arguments: named parameters (<c>-Path x</c>, <c>-Path:x</c>, unambiguous prefixes such as <c>-Dest</c>) by
/// canonical lower-case name, and positional words. An unknown parameter is taken as a switch, so the word after it stays
/// positional (and is checked as a path).
/// </summary>
internal sealed record PowerShellArguments(IReadOnlyDictionary<string, List<string>> Named, IReadOnlyList<string> Positional, IReadOnlySet<string> Switches)
{
    private static readonly string[] ValueParameters =
        ["path", "literalpath", "filepath", "destination", "newname", "name", "value", "encoding", "itemtype", "filter", "include", "exclude", "stream", "inputobject", "width", "variable", "credential", "target", "argumentlist", "delimiter"];

    private static readonly Dictionary<string, string> ParameterAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pspath"] = "literalpath",
        ["lp"] = "literalpath",
        ["args"] = "argumentlist",
    };

    public static PowerShellArguments Parse(IReadOnlyList<string> arguments)
    {
        Dictionary<string, List<string>> named = [];
        List<string> positional = [];
        HashSet<string> switches = [];
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (argument.Length < 2 || argument[0] != '-' || !char.IsAsciiLetter(argument[1]))
            {
                positional.Add(argument);
                continue;
            }

            int colon = argument.IndexOf(':', StringComparison.Ordinal);
            string given = (colon > 0 ? argument[1..colon] : argument[1..]).ToLowerInvariant();
            string? parameter = Canonical(given);
            if (colon > 0)
            {
                Add(named, parameter ?? given, argument[(colon + 1)..]);
            }
            else if (parameter is not null && index + 1 < arguments.Count)
            {
                Add(named, parameter, arguments[++index]);
            }
            else
            {
                switches.Add(given);
            }
        }

        return new PowerShellArguments(named, positional, switches);
    }

    public IReadOnlyList<string> Values(params string[] parameters) =>
        [.. parameters.SelectMany(parameter => Named.TryGetValue(parameter, out List<string>? values) ? values : [])];

    public bool HasSwitch(string name) => Switches.Any(given => name.StartsWith(given, StringComparison.Ordinal));

    private static string? Canonical(string given) =>
        ParameterAliases.TryGetValue(given, out string? alias) ? alias : ValueParameters.FirstOrDefault(parameter => parameter.StartsWith(given, StringComparison.Ordinal));

    private static void Add(Dictionary<string, List<string>> named, string parameter, string value)
    {
        if (!named.TryGetValue(parameter, out List<string>? values))
        {
            values = [];
            named[parameter] = values;
        }

        values.Add(value);
    }
}
