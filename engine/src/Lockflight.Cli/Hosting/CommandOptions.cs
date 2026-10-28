namespace Lockflight.Cli.Hosting;

/// <summary>
/// The <c>--name value</c> options and <c>--flag</c> switches of one command, parsed strictly against what that command accepts: an
/// unknown option, a stray argument, a repeated option or an option without its value is a usage error (exit 4), never ignored — a
/// typo such as <c>--profil pr</c> must not quietly run the default.
/// </summary>
internal sealed class CommandOptions(IReadOnlyDictionary<string, string> values, IReadOnlySet<string> flags)
{
    /// <summary>The option's value; null when the option was not given.</summary>
    public string? Value(string name) => values.GetValueOrDefault(name);

    public bool Flag(string name) => flags.Contains(name);

    /// <summary>Parses <paramref name="args"/> for a command that accepts <paramref name="valueOptions"/> and <paramref name="flagOptions"/>.</summary>
    public static CommandOptionsParse Parse(IReadOnlyList<string> args, IReadOnlyCollection<string> valueOptions, IReadOnlyCollection<string> flagOptions)
    {
        Dictionary<string, string> parsedValues = new(StringComparer.Ordinal);
        HashSet<string> parsedFlags = new(StringComparer.Ordinal);
        for (int index = 0; index < args.Count; index++)
        {
            string name = args[index];
            if (parsedValues.ContainsKey(name) || parsedFlags.Contains(name))
            {
                return CommandOptionsParse.Failed($"{name} is given more than once");
            }

            if (flagOptions.Contains(name))
            {
                parsedFlags.Add(name);
            }
            else if (!valueOptions.Contains(name))
            {
                return CommandOptionsParse.Failed(Unknown(name, valueOptions, flagOptions));
            }
            else if (index + 1 >= args.Count || args[index + 1].Length == 0 || args[index + 1].StartsWith('-'))
            {
                string got = index + 1 < args.Count ? $" (got '{args[index + 1]}', which is not a value)" : string.Empty;
                return CommandOptionsParse.Failed($"{name} needs a value{got}");
            }
            else
            {
                parsedValues[name] = args[++index];
            }
        }

        return CommandOptionsParse.Parsed(new CommandOptions(parsedValues, parsedFlags));
    }

    /// <summary>Parses what follows a command's words against the options its descriptor accepts, like the overload below.</summary>
    public static Task<CommandOptions?> ParseOrReportAsync(CommandArguments arguments, TextWriter error, CancellationToken cancellationToken) =>
        ParseOrReportAsync(arguments.Command.Name, arguments.Rest, arguments.Command.ValueOptions, arguments.Command.FlagOptions, error, cancellationToken);

    /// <summary>Parses like <see cref="Parse"/>; on a usage error writes it to <paramref name="error"/> and returns null (exit 4).</summary>
    public static async Task<CommandOptions?> ParseOrReportAsync(
        string command, IReadOnlyList<string> args, IReadOnlyCollection<string> valueOptions, IReadOnlyCollection<string> flagOptions, TextWriter error, CancellationToken cancellationToken)
    {
        CommandOptionsParse parse = Parse(args, valueOptions, flagOptions);
        if (parse.Options is CommandOptions options)
        {
            return options;
        }

        await error.WriteLineAsync($"lockflight {command}: {parse.Error}. Run 'lockflight --help' for its options.".AsMemory(), cancellationToken);
        return null;
    }

    private static string Unknown(string name, IReadOnlyCollection<string> valueOptions, IReadOnlyCollection<string> flagOptions)
    {
        string[] accepted = [.. valueOptions, .. flagOptions];
        string what = name.StartsWith('-') ? $"unknown option '{name}'" : $"unexpected argument '{name}'";
        return accepted.Length == 0 ? $"{what}; this command takes no options" : $"{what}; use {string.Join(", ", accepted)}";
    }
}
