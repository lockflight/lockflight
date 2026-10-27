namespace Lockflight.Cli.Hooks;

/// <summary>
/// Text filters that write a file they are given: <c>sort -o FILE</c> (<c>--output=FILE</c>, <c>-oFILE</c>), <c>uniq INPUT OUTPUT</c>,
/// <c>awk -i inplace</c> (gawk's in-place library) and <c>yq -i</c>/<c>--inplace</c>, which rewrite their file operands.
/// </summary>
internal static class FilterCommandTargets
{
    private const string InPlaceLibrary = "inplace";

    private static readonly string[] UniqOptionsWithValue = ["-f", "-s", "-w", "--skip-fields", "--skip-chars", "--check-chars", "--group", "--all-repeated"];
    private static readonly HashSet<string> AwkOptionsWithValue = ["-v", "-F", "-f", "-i", "-l", "-E", "--assign", "--field-separator", "--file", "--include", "--load", "--exec"];
    private static readonly HashSet<string> YqOptionsWithValue = ["-o", "--output-format", "-p", "--input-format", "-I", "--indent", "--from-file", "--expression", "--split-exp"];

    public static IReadOnlySet<string> Programs { get; } = new HashSet<string>(["sort", "uniq", "awk", "gawk", "yq"]);

    public static CommandWrites Of(string program, IReadOnlyList<string> arguments) => program switch
    {
        "sort" => CommandWrites.OfFiles(ShellArguments.OptionValue(arguments, "-o", "--output") is string output ? [output] : []),
        "uniq" => CommandWrites.OfFiles([.. ShellArguments.Operands(arguments, UniqOptionsWithValue).Skip(1).Take(1)]),
        "yq" => CommandWrites.OfFiles(YqFiles(arguments)),
        _ => CommandWrites.OfFiles(AwkFiles(arguments)),
    };

    /// <summary><c>awk -i inplace [-f prog | 'prog'] FILE…</c>: the operands after the program, except <c>var=value</c> assignments.</summary>
    private static List<string> AwkFiles(IReadOnlyList<string> arguments)
    {
        bool inPlace = false;
        bool programByOption = false;
        List<string> operands = [];
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (argument == ShellArguments.EndOfOptions)
            {
                operands.AddRange(arguments.Skip(index + 1));
                break;
            }

            string? library = argument == "-i" && index + 1 < arguments.Count ? arguments[index + 1]
                : argument.StartsWith("--include=", StringComparison.Ordinal) ? argument["--include=".Length..]
                : argument.StartsWith("-i", StringComparison.Ordinal) && argument.Length > 2 ? argument[2..]
                : null;
            inPlace |= library is InPlaceLibrary or InPlaceLibrary + ".awk";
            programByOption |= argument is "-f" or "--file" or "-E" or "--exec" || argument.StartsWith("--file=", StringComparison.Ordinal);
            if (AwkOptionsWithValue.Contains(argument))
            {
                index++;
            }
            else if (!argument.StartsWith('-') || argument == "-")
            {
                operands.Add(argument);
            }
        }

        return inPlace ? [.. operands.Skip(programByOption ? 0 : 1).Where(operand => !ShellAssignment.IsAssignment(operand))] : [];
    }

    /// <summary><c>yq -i 'expression' FILE…</c>; the expression is the first operand unless given by option.</summary>
    private static List<string> YqFiles(IReadOnlyList<string> arguments)
    {
        if (!ShellArguments.HasFlag(arguments, "i", "--inplace"))
        {
            return [];
        }

        List<string> operands = ShellArguments.Operands(arguments, YqOptionsWithValue);
        bool expressionByOption = arguments.Any(argument => argument is "--from-file" or "--expression");
        return operands.Count == 1 || expressionByOption ? operands : [.. operands.Skip(1)];
    }
}
