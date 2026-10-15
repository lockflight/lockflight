namespace Lockflight.Cli.Hooks;

/// <summary>
/// <c>sed</c> and <c>perl</c> editing files in place, in every flag spelling: <c>-i</c>, <c>-i.bak</c>, <c>-Ei</c>, <c>-ni</c>,
/// <c>--in-place[=suffix]</c>, <c>perl -pi</c>, <c>-0pi</c>. The script is the first operand unless given by an option
/// (<c>-e</c>, <c>-f</c>, <c>--expression</c>).
/// </summary>
internal static class InPlaceEditTargets
{
    private const string InPlace = "--in-place";

    private static readonly HashSet<string> SedLongOptionsWithValue = ["--expression", "--file", "--line-length"];

    public static IReadOnlySet<string> Programs { get; } = new HashSet<string>(["sed", "perl"]);

    public static CommandWrites Of(string program, IReadOnlyList<string> arguments) =>
        CommandWrites.OfFiles(program == "sed" ? SedFiles(arguments) : PerlFiles(arguments));

    private static List<string> SedFiles(IReadOnlyList<string> arguments)
    {
        bool inPlace = false;
        bool scriptByOption = false;
        List<string> operands = [];
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (argument == ShellArguments.EndOfOptions)
            {
                operands.AddRange(arguments.Skip(index + 1));
                break;
            }

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                inPlace |= argument == InPlace || argument.StartsWith(InPlace + "=", StringComparison.Ordinal);
                scriptByOption |= argument.StartsWith("--expression", StringComparison.Ordinal) || argument.StartsWith("--file", StringComparison.Ordinal);
                index += SedLongOptionsWithValue.Contains(argument) ? 1 : 0;
            }
            else if (argument.Length > 1 && argument[0] == '-')
            {
                (bool clusterInPlace, bool clusterScript, bool takesNext) = ReadSedCluster(argument[1..]);
                inPlace |= clusterInPlace;
                scriptByOption |= clusterScript;
                index += takesNext ? 1 : 0;
            }
            else
            {
                operands.Add(argument);
            }
        }

        return inPlace ? [.. operands.Skip(scriptByOption ? 0 : 1)] : [];
    }

    /// <summary>
    /// A short-flag cluster: <c>i</c> ends it (the rest is the backup suffix); <c>e</c>, <c>f</c> and <c>l</c> take the rest of the
    /// cluster or, when nothing follows, the next word.
    /// </summary>
    private static (bool InPlace, bool Script, bool TakesNext) ReadSedCluster(string cluster)
    {
        for (int position = 0; position < cluster.Length; position++)
        {
            char flag = cluster[position];
            if (flag == 'i')
            {
                return (true, false, false);
            }

            if (flag is 'e' or 'f' or 'l')
            {
                return (false, flag != 'l', position == cluster.Length - 1);
            }
        }

        return (false, false, false);
    }

    /// <summary>
    /// <c>perl -i</c> in any cluster edits its file operands. The script is the word after a cluster ending in <c>e</c>/<c>E</c> (before
    /// any <c>i</c>, whose rest is the suffix), else the first operand.
    /// </summary>
    private static List<string> PerlFiles(IReadOnlyList<string> arguments)
    {
        bool inPlace = false;
        bool inlineScript = false;
        List<string> operands = [];
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (argument == ShellArguments.EndOfOptions)
            {
                operands.AddRange(arguments.Skip(index + 1));
                break;
            }

            if (argument.Length > 1 && argument.StartsWith('-') && !argument.StartsWith(ShellArguments.EndOfOptions, StringComparison.Ordinal))
            {
                string cluster = argument[1..];
                int inPlaceFlag = cluster.IndexOf('i', StringComparison.Ordinal);
                string flags = inPlaceFlag >= 0 ? cluster[..(inPlaceFlag + 1)] : cluster;
                inPlace |= inPlaceFlag >= 0;
                if (flags.EndsWith('e') || flags.EndsWith('E'))
                {
                    inlineScript = true;
                    index++;
                }
            }
            else
            {
                operands.Add(argument);
            }
        }

        return inPlace ? [.. operands.Skip(inlineScript ? 0 : 1)] : [];
    }
}
