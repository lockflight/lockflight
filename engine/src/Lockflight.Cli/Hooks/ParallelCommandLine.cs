using System.Globalization;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// The jobs GNU <c>parallel</c> runs, written as one POSIX command string: the command template (its words joined, as
/// <c>parallel</c> passes it to a shell) once per combination of the <c>:::</c> argument groups, with each replacement string
/// (<c>{}</c>, <c>{1}</c>, <c>{.}</c>, <c>{/}</c>, <c>-I STR</c>, …) replaced by its shell-quoted value, or the values appended when
/// the template has none. Without a template the values are the commands. Arguments read from stdin, <c>::::</c> or
/// <c>-a FILE</c> are unknown: they stand for a command substitution, so a writer behind them has hidden targets.
/// </summary>
internal static partial class ParallelCommandLine
{
    /// <summary>The most jobs written out; the arguments of a run with more of them count as unknown.</summary>
    public const int MaxJobs = 256;

    private const string ShortWithValue = "jPSaIEdCnNLslJ";
    private const string ArgumentsGroup = ":::";
    private const string LinkedArgumentsGroup = ":::+";
    private const string FileGroup = "::::";
    private const string LinkedFileGroup = "::::+";
    private const string JobNumber = "1";

    /// <summary>Stands for arguments the command line does not show.</summary>
    private static readonly string UnknownArguments = $"\"$(cat)\"";

    private static readonly HashSet<string> LongWithValue =
    [
        "--jobs", "--max-procs", "--sshlogin", "--sshloginfile", "--slf", "--arg-file", "--colsep", "--delimiter", "--max-args",
        "--max-lines", "--max-chars", "--joblog", "--results", "--res", "--tmpdir", "--tempdir", "--workdir", "--wd", "--env",
        "--retries", "--timeout", "--delay", "--memfree", "--load", "--halt", "--halt-on-error", "--basefile", "--bf",
        "--transferfile", "--tf", "--return", "--tagstring", "--tag-string", "--sshdelay", "--block", "--block-size",
        "--recstart", "--recend", "--rpl", "--profile", "--termseq", "--ssh", "--limit", "--nice", "--eof", "--replace",
        "--extensionreplace", "--er", "--basenamereplace", "--bnr", "--dirnamereplace", "--dnr", "--seqreplace",
    ];

    private static readonly HashSet<string> Separators = [ArgumentsGroup, LinkedArgumentsGroup, FileGroup, LinkedFileGroup];
    private static readonly string[] ArgumentFiles = ["-a", "--arg-file"];
    private static readonly string[] Replacements = ["-I", "--replace"];
    private static readonly string[] Pipes = ["--pipe", "--pipepart", "--spreadstdin"];

    public static NestedShellCommand Of(IReadOnlyList<string> arguments)
    {
        RunnerOptions options = RunnerOptions.Read(arguments, ShortWithValue, LongWithValue, permute: false);
        List<string> template = [.. options.Operands.TakeWhile(operand => !Separators.Contains(operand))];
        string command = string.Join(' ', template);
        if (options.Has(Pipes))
        {
            return Nested(command, null);
        }

        IReadOnlyList<string> replacements = options.Values(Replacements);
        string? replacement = replacements.Count > 0 ? replacements[^1] : null;
        List<List<string>>? groups = options.Has(ArgumentFiles) ? null : Groups([.. options.Operands.Skip(template.Count)]);
        long jobs = groups?.Aggregate(1L, (product, group) => Math.Min(product * Math.Max(group.Count, 1), MaxJobs + 1L)) ?? 0;
        if (groups is null || jobs > MaxJobs)
        {
            // Arguments from stdin or a file, or too many jobs to write out: a writer behind them has targets that are not listed.
            return Nested(Job(command, replacement, [UnknownArguments]), null);
        }

        return Nested(string.Join('\n', Combinations(groups).Select(values => Job(command, replacement, values))), null);
    }

    /// <summary>The <c>:::</c> groups after the template; null when any group is read from a file (<c>::::</c>) or there are none.</summary>
    private static List<List<string>>? Groups(List<string> rest)
    {
        List<List<string>> groups = [];
        foreach (string word in rest)
        {
            if (word is FileGroup or LinkedFileGroup)
            {
                return null;
            }

            if (Separators.Contains(word))
            {
                groups.Add([]);
            }
            else
            {
                groups[^1].Add(word);
            }
        }

        return groups.Count > 0 ? groups : null;
    }

    private static IEnumerable<IReadOnlyList<string>> Combinations(List<List<string>> groups)
    {
        IEnumerable<IReadOnlyList<string>> combinations = [[]];
        foreach (List<string> group in groups.Where(group => group.Count > 0))
        {
            combinations = combinations.SelectMany(prefix => group.Select(value => (IReadOnlyList<string>)[.. prefix, value]));
        }

        return combinations;
    }

    /// <summary>One job from the values of a combination: replaced into the template, appended to it, or run themselves.</summary>
    private static string Job(string template, string? replacement, IReadOnlyList<string> values)
    {
        string appended = $"{template} {string.Join(' ', values.Select(value => Quoted(value, string.Empty)))}";
        if (template.Length == 0)
        {
            return string.Join(' ', values);
        }

        if (replacement is not null)
        {
            return template.Contains(replacement, StringComparison.Ordinal)
                ? template.Replace(replacement, string.Join(' ', values.Select(value => Quoted(value, string.Empty))), StringComparison.Ordinal)
                : appended;
        }

        bool replaced = false;
        string job = ReplacementString().Replace(template, match =>
        {
            replaced = true;
            return Replace(match, values);
        });
        return replaced ? job : appended;
    }

    private static string Replace(Match match, IReadOnlyList<string> values)
    {
        string modifier = match.Groups["modifier"].Value;
        if (modifier is "#" or "%")
        {
            return JobNumber;
        }

        IEnumerable<string> selected = match.Groups["position"].Success
            && int.TryParse(match.Groups["position"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int position)
            ? values.Skip(position - 1).Take(1)
            : values;
        return string.Join(' ', selected.Select(value => Quoted(value, modifier)));
    }

    private static string Modified(string value, string modifier)
    {
        string name = value[(value.LastIndexOf('/') + 1)..];
        return modifier switch
        {
            "." => WithoutExtension(value),
            "/" => name,
            "//" => value.Contains('/', StringComparison.Ordinal) ? value[..value.LastIndexOf('/')] : ShellArguments.CurrentDirectory,
            "/." => WithoutExtension(name),
            _ => value,
        };
    }

    private static string WithoutExtension(string value)
    {
        int dot = value.LastIndexOf('.');
        return dot > value.LastIndexOf('/') + 1 ? value[..dot] : value;
    }

    /// <summary>A value, modified, as parallel quotes it; <see cref="UnknownArguments"/> stays the substitution it is.</summary>
    private static string Quoted(string value, string modifier) => value == UnknownArguments ? value : ShellWordQuoting.Word(Modified(value, modifier));

    private static NestedShellCommand Nested(string command, string? hidden) => new(command, ShellDialect.Posix, SameShell: false, hidden);

    [GeneratedRegex(@"\{(?<position>\d+)?(?<modifier>\.|//|/\.|/|#|%)?\}")]
    private static partial Regex ReplacementString();
}
