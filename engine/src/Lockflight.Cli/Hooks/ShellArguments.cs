namespace Lockflight.Cli.Hooks;

/// <summary>Argument helpers shared by the writers' parsers.</summary>
internal static class ShellArguments
{
    public const string EndOfOptions = "--";
    public const string CurrentDirectory = ".";

    /// <summary>Non-option words, skipping the value after each option in <paramref name="optionsWithValue"/>; everything after <c>--</c> is an operand.</summary>
    public static List<string> Operands(IReadOnlyList<string> arguments, IReadOnlyCollection<string> optionsWithValue)
    {
        List<string> operands = [];
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (argument == EndOfOptions)
            {
                operands.AddRange(arguments.Skip(index + 1));
                break;
            }

            if (optionsWithValue.Contains(argument))
            {
                index++;
            }
            else if (!argument.StartsWith('-') || argument == "-")
            {
                operands.Add(argument);
            }
        }

        return operands;
    }

    /// <summary>Whether a short-option cluster (<c>-rf</c>) or a long option among the arguments is one of the given flags.</summary>
    public static bool HasFlag(IReadOnlyList<string> arguments, string shortFlags, params string[] longFlags) =>
        arguments.TakeWhile(argument => argument != EndOfOptions).Any(argument =>
            longFlags.Contains(argument)
            || (argument.Length > 1 && argument[0] == '-' && argument[1] != '-' && argument[1..].Any(flag => shortFlags.Contains(flag, StringComparison.Ordinal))));

    /// <summary>The value of <c>-x VALUE</c>, <c>-xVALUE</c>, <c>--long VALUE</c> or <c>--long=VALUE</c>; null when absent.</summary>
    public static string? OptionValue(IReadOnlyList<string> arguments, string? shortOption, string? longOption)
    {
        for (int index = 0; index < arguments.Count && arguments[index] != EndOfOptions; index++)
        {
            string argument = arguments[index];
            if ((argument == shortOption || argument == longOption) && index + 1 < arguments.Count)
            {
                return arguments[index + 1];
            }

            if (longOption is not null && argument.StartsWith(longOption + "=", StringComparison.Ordinal))
            {
                return argument[(longOption.Length + 1)..];
            }

            if (shortOption is not null && argument.Length > shortOption.Length && argument.StartsWith(shortOption, StringComparison.Ordinal) && !argument.StartsWith("--", StringComparison.Ordinal))
            {
                return argument[shortOption.Length..];
            }
        }

        return null;
    }

    /// <summary>The path inside a directory, keeping the separator the directory was written with.</summary>
    public static string Join(string directory, string name)
    {
        if (directory == CurrentDirectory)
        {
            return name;
        }

        char separator = directory.Contains('\\', StringComparison.Ordinal) && !directory.Contains('/', StringComparison.Ordinal) ? '\\' : '/';
        return $"{directory.TrimEnd('/', '\\')}{separator}{name}";
    }

    /// <summary>The last segment of a path (<c>a/b/</c> → <c>b</c>); null when it names a directory's contents (<c>.</c>, <c>..</c>, <c>dir/.</c>).</summary>
    public static string? BaseName(string path)
    {
        string trimmed = path.TrimEnd('/', '\\');
        string name = trimmed[(trimmed.LastIndexOfAny(['/', '\\']) + 1)..];
        return name is "" or "." or ".." ? null : name;
    }

    /// <summary>The directory part of a path as written; <c>.</c> when there is none.</summary>
    public static string Parent(string path)
    {
        int separator = path.TrimEnd('/', '\\').LastIndexOfAny(['/', '\\']);
        return separator < 0 ? CurrentDirectory : separator == 0 ? path[..1] : path[..separator];
    }
}
