namespace Lockflight.Cli.Hooks;

/// <summary>
/// The options and operands of a program that runs another command (<c>script</c>, <c>watch</c>, <c>parallel</c>), read the way
/// getopt does: short clusters (<c>-qa</c>) whose value flag takes the rest of the word or the next word (<c>-n1</c>, <c>-n 1</c>),
/// long options with <c>=value</c> or a next-word value, and <c>--</c> ending the options.
/// </summary>
internal sealed record RunnerOptions(IReadOnlyList<RunnerOption> Options, IReadOnlyList<string> Operands)
{
    /// <summary>
    /// Reads <paramref name="arguments"/>. Without <paramref name="permute"/> the options end at the first operand (BSD getopt and
    /// the runners whose command follows); with it, options after operands are read too (GNU getopt).
    /// </summary>
    public static RunnerOptions Read(IReadOnlyList<string> arguments, string shortWithValue, IReadOnlySet<string> longWithValue, bool permute)
    {
        List<RunnerOption> options = [];
        List<string> operands = [];
        int index = 0;
        while (index < arguments.Count)
        {
            string argument = arguments[index];
            if (argument == ShellArguments.EndOfOptions)
            {
                index++;
                break;
            }

            if (argument.Length < 2 || argument[0] != '-')
            {
                if (!permute)
                {
                    break;
                }

                operands.Add(argument);
                index++;
                continue;
            }

            index = argument.StartsWith(ShellArguments.EndOfOptions, StringComparison.Ordinal)
                ? ReadLong(arguments, index, longWithValue, options)
                : ReadCluster(arguments, index, shortWithValue, options);
        }

        operands.AddRange(arguments.Skip(index));
        return new RunnerOptions(options, operands);
    }

    public bool Has(params string[] names) => Options.Any(option => names.Contains(option.Name));

    /// <summary>The values given to any of the named options, in order.</summary>
    public IReadOnlyList<string> Values(params string[] names) =>
        [.. Options.Where(option => option.Value is not null && names.Contains(option.Name)).Select(option => option.Value!)];

    private static int ReadLong(IReadOnlyList<string> arguments, int index, IReadOnlySet<string> longWithValue, List<RunnerOption> options)
    {
        string argument = arguments[index];
        int equals = argument.IndexOf('=', StringComparison.Ordinal);
        if (equals > 0)
        {
            options.Add(new RunnerOption(argument[..equals], argument[(equals + 1)..]));
            return index + 1;
        }

        if (longWithValue.Contains(argument) && index + 1 < arguments.Count)
        {
            options.Add(new RunnerOption(argument, arguments[index + 1]));
            return index + 2;
        }

        options.Add(new RunnerOption(argument, null));
        return index + 1;
    }

    private static int ReadCluster(IReadOnlyList<string> arguments, int index, string shortWithValue, List<RunnerOption> options)
    {
        string argument = arguments[index];
        for (int position = 1; position < argument.Length; position++)
        {
            string name = $"-{argument[position]}";
            if (!shortWithValue.Contains(argument[position], StringComparison.Ordinal))
            {
                options.Add(new RunnerOption(name, null));
                continue;
            }

            if (position + 1 < argument.Length)
            {
                options.Add(new RunnerOption(name, argument[(position + 1)..]));
                return index + 1;
            }

            options.Add(new RunnerOption(name, index + 1 < arguments.Count ? arguments[index + 1] : null));
            return index + 2;
        }

        return index + 1;
    }
}
