using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Best-effort extraction of what a shell command line would write or delete: redirections in every fd form, the writers known to
/// <see cref="CommandWriters"/>, commands inside substitutions (<c>$( … )</c>, backticks), subshells, PowerShell script blocks, and
/// the command strings of other shells (<see cref="NestedShellCommand"/>), and the words a default (<c>${X:-word}</c>) may stand
/// for. Relative paths are checked under every directory a <c>cd</c> may have moved to (see <see cref="ShellLocations"/>). Targets
/// computed at run time (substitutions, pipelines into PowerShell writers, .NET file APIs, names read by <c>while read</c> from a
/// pipe, a command string that is itself a variable or a substitution) are reported as hidden. A guardrail against accidents, not a
/// boundary: interpreters (<c>python -c</c>, <c>node -e</c>), scripts and build tools are invisible to it.
/// </summary>
internal sealed partial class ShellWriteTargets(ShellWritesBuilder writes)
{
    private const int MaxNesting = 4;

    /// <summary>The last simple command seen: the left side of a pipe when the next one reads it; null after a compound command.</summary>
    private ShellCommand? previous;

    /// <summary>
    /// Variables a <c>while read NAME</c> fed by a pipe sets from its input, with the directories that input is confined to (null
    /// when it can be anything).
    /// </summary>
    private readonly Dictionary<string, IReadOnlyList<string>?> inputVariables = new(StringComparer.Ordinal);

    /// <summary>Variables a <c>for NAME in WORD…</c> sets, with its words: a reference to one stands for each of them.</summary>
    private readonly Dictionary<string, IReadOnlyList<string>> loopVariables = new(StringComparer.Ordinal);

    /// <summary>Set once the command line does something after which no variable is known (<see cref="ShellLiteralVariables"/>).</summary>
    private readonly StrongBox<bool> variablesUntracked = new();

    public static ShellWrites Find(string command, ShellDialect dialect)
    {
        ShellWritesBuilder writes = new();
        new ShellWriteTargets(writes).Scan(command, dialect, new ShellLocations(dialect), depth: 0);
        return writes.Build();
    }

    private void Scan(string command, ShellDialect dialect, ShellLocations start, int depth)
    {
        ShellLocations locations = start;
        ShellLiteralVariables variables = new(dialect, variablesUntracked);
        List<ShellWord> segment = [];
        Stack<OpenGroup> groups = new();
        ShellTokenKind? redirect = null;
        ShellTokenKind? previousKind = null;
        string? before = null;
        foreach (ShellToken token in ShellTokenizer.Tokenize(command, dialect))
        {
            ShellTokenKind? kindBefore = previousKind;
            previousKind = token.Kind;
            if (token.Kind == ShellTokenKind.HereText)
            {
                segment.Add(new ShellWord(token.Text, token) { StandardInput = true });
                redirect = null;
                continue;
            }

            if (token.IsWord)
            {
                AddWord(variables.Word(token), ref redirect, segment);
                continue;
            }

            ShellTokenKind? pendingRedirect = redirect;
            redirect = token.Kind is ShellTokenKind.WriteRedirect or ShellTokenKind.InputRedirect ? token.Kind : null;
            if (token.Kind is ShellTokenKind.Separator or ShellTokenKind.SubshellOpen)
            {
                bool definesFunction = token.Kind == ShellTokenKind.SubshellOpen && segment.Count > 0;
                Run(segment, dialect, locations, variables, depth, before, after: token.Text);
                segment = [];
                before = token.Text;
                variables.EndList(token.Text);
                bool arithmetic = token.Kind == ShellTokenKind.SubshellOpen
                    && kindBefore is ShellTokenKind.SubshellOpen or ShellTokenKind.SubstitutionOpen;
                if (definesFunction || arithmetic)
                {
                    // "name()" defines a function, which may set variables when it is called; "((" and "$((" may assign.
                    variables.Untrack();
                }
            }

            if (token.Kind is ShellTokenKind.SubshellOpen or ShellTokenKind.SubstitutionOpen or ShellTokenKind.GroupOpen)
            {
                // A subshell or substitution runs in a copy of the shell: its cd and its assignments end with it. A PowerShell group
                // does not.
                // "> $(cmd)": the substitution's output is the redirect target.
                groups.Push(new OpenGroup(token.Kind, segment, locations, variables, before, pendingRedirect));
                before = null;
                segment = [];
                locations = token.Kind == ShellTokenKind.GroupOpen ? locations : locations.Copy();
                variables = token.Kind == ShellTokenKind.GroupOpen ? variables : variables.Copy();
            }
            else if (token.Kind == ShellTokenKind.Close)
            {
                Run(segment, dialect, locations, variables, depth, before, after: null);
                segment = [];
                if (groups.TryPop(out OpenGroup? group))
                {
                    before = group.Before;
                    redirect = group.Redirect;
                    segment = group.Kind == ShellTokenKind.SubshellOpen ? [] : group.Segment;
                    locations = group.Locations;
                    variables = group.Variables;
                    if (group.Kind == ShellTokenKind.GroupOpen)
                    {
                        // A PowerShell group's value stands for one word; the tokenizer adds no placeholder for it.
                        ShellToken placeholder = new(ShellTokenKind.Word, ShellTokenizer.SubstitutionPlaceholder);
                        AddWord(new ShellWord(placeholder.Text, placeholder), ref redirect, segment);
                    }
                }
            }
        }

        Run(segment, dialect, locations, variables, depth, before, after: null);
    }

    private void Run(
        List<ShellWord> segment, ShellDialect dialect, ShellLocations locations, ShellLiteralVariables variables, int depth, string? before, string? after)
    {
        segment = [.. segment.Select(WithLoopValues)];
        if (BindLoopVariable(segment))
        {
            // A loop sets its variable to each word in turn: no literal value of it is known after it either.
            variables.Untrack();
            previous = null;
            return;
        }

        // A variable the statement assigns is expanded in its redirections with the value from before it.
        bool assigns = segment.Any(word => word.Assignment is not null && !word.RedirectTarget);
        foreach (ShellWord target in segment.Where(word => word.RedirectTarget))
        {
            string text = assigns ? target.Source.Text : target.Text;
            if (!AddThroughInputVariables([text], "a redirection", CommandWrites.OfFiles([text]), locations))
            {
                writes.AddFile(text, locations);
            }

            foreach (string alternative in target.Alternatives)
            {
                writes.AddFile(alternative, locations);
            }
        }

        List<ShellWord> commandWords = [.. segment.Where(word => !word.RedirectTarget && !word.StandardInput)];
        string? hereText = segment.LastOrDefault(word => word.StandardInput)?.Text;
        variables.Run(commandWords, before, after);
        List<string> words = [.. commandWords.Select(word => word.Text)];
        bool piped = before is "|" or "|&";
        if (words.FirstOrDefault(DotnetFileApi.Writes) is string apiCall)
        {
            writes.Hide($"'{apiCall}' (a .NET file API call)");
            return;
        }

        if (ShellCommand.Parse(words) is not ShellCommand parsed)
        {
            // The end of a compound command ("}", ")"): what it pipes on is not one simple command.
            previous = null;
            return;
        }

        ShellCommand command = parsed with { PipedInput = piped };
        ShellCommand? upstream = piped ? previous : null;
        previous = command;
        int programIndex = command.ProgramIndex;
        writes.AddHumanOnly(HumanOnlyCommands.FindInWords(words, honorHelp: true), HumanOnlyCommands.FindInWords(words, honorHelp: false));
        if (depth > 0 && programIndex >= 0 && IsExpansionOnly(commandWords[programIndex]))
        {
            // eval "$X", bash -c "$CMD": the command itself is computed when it runs.
            writes.Hide($"{commandWords[programIndex].Text.Replace(ShellTokenizer.SubstitutionPlaceholder, "$(…)", StringComparison.Ordinal)} (a command string computed when it runs)");
            return;
        }

        if (ShellLocations.Changers.Contains(command.Program))
        {
            locations.ChangeDirectory(command.Program, command.Arguments);
            return;
        }

        StandardInput input = hereText is not null ? new StandardInput(hereText, null) : piped ? StandardInput.Piped(upstream) : StandardInput.None;
        if (NestedShellCommand.Find(command, input) is NestedShellCommand nested)
        {
            if (nested.Hidden is not null || depth >= MaxNesting)
            {
                writes.Hide(nested.Hidden ?? $"{command.Program} nested {MaxNesting} levels deep");
                return;
            }

            Scan(nested.Command, nested.Dialect, nested.SameShell ? locations : locations.Copy(), depth + 1);
            return;
        }

        if (piped && command.Program == "read" && words.Take(programIndex).Any(word => word is "while" or "until"))
        {
            BindInputVariables(command.Arguments, upstream);
            return;
        }

        if (command.ArgumentsFromInput && upstream is not null && CommandWriters.IsWriter(command, dialect)
            && PipedFileListSource.Directories(upstream) is IReadOnlyList<string> listed)
        {
            // grep -l … DIR | xargs sed -i …: the files come from under DIR, so the writer rewrites at most that tree.
            CommandWrites visible = CommandWriters.Of(command with { ArgumentsFromInput = false }, dialect);
            writes.Add(visible with { Trees = [.. visible.Trees, .. listed] }, locations);
            return;
        }

        CommandWrites commandWrites = CommandWriters.Of(command, dialect);
        if (!CommandWriters.IsWriter(command, dialect) || !AddThroughInputVariables(command.Arguments, command.Program, commandWrites, locations))
        {
            writes.Add(commandWrites, locations);
        }

        AddAlternatives(commandWords, words, piped, dialect, locations);
    }

    /// <summary>Each other value a word may stand for (<c>${X:-word}</c>) is checked in the command as well.</summary>
    private void AddAlternatives(List<ShellWord> commandWords, List<string> words, bool piped, ShellDialect dialect, ShellLocations locations)
    {
        for (int index = 0; index < commandWords.Count; index++)
        {
            foreach (string alternative in commandWords[index].Alternatives)
            {
                List<string> alternativeWords = [.. words];
                alternativeWords[index] = alternative;
                if (ShellCommand.Parse(alternativeWords) is ShellCommand command)
                {
                    writes.Add(CommandWriters.Of(command with { PipedInput = piped }, dialect), locations);
                }
            }
        }
    }

    /// <summary><c>for NAME in WORD…</c> (the list up to its <c>;</c> or newline): the name holds each word in turn.</summary>
    private bool BindLoopVariable(List<ShellWord> segment)
    {
        List<string> words = [.. segment.Where(word => !word.RedirectTarget && !word.StandardInput).Select(word => word.Text)];
        int loop = words.IndexOf("for");
        if (loop < 0 || loop + 2 >= words.Count || words[loop + 2] != "in" || !ShellAssignment.IsAssignment(words[loop + 1] + "="))
        {
            return false;
        }

        loopVariables[words[loop + 1]] = words[(loop + 3)..];
        return true;
    }

    /// <summary>The word with one more alternative per value of each loop variable it refers to (<c>$f</c>, <c>${f}</c>).</summary>
    private ShellWord WithLoopValues(ShellWord word)
    {
        if (loopVariables.Count == 0 || word.Source.LiteralDollar || !word.Text.Contains('$', StringComparison.Ordinal))
        {
            return word;
        }

        List<string> alternatives = [.. word.Alternatives];
        foreach ((string name, IReadOnlyList<string> values) in loopVariables)
        {
            Regex reference = VariableReference(name);
            if (reference.IsMatch(word.Text))
            {
                alternatives.AddRange(values.Select(value => reference.Replace(word.Text, _ => value)));
            }
        }

        return alternatives.Count == word.Alternatives.Count ? word : word with { Alternatives = alternatives };
    }

    /// <summary><c>… | while read [-r] NAME…</c>: each name holds a line of the input, like an argument behind <c>xargs</c>.</summary>
    private void BindInputVariables(IReadOnlyList<string> arguments, ShellCommand? upstream)
    {
        IReadOnlyList<string>? directories = upstream is null ? null : PipedFileListSource.Directories(upstream);
        foreach (string name in ShellArguments.Operands(arguments, ["-a", "-d", "-i", "-n", "-N", "-p", "-t", "-u"]))
        {
            inputVariables[name] = directories;
        }
    }

    /// <summary>
    /// A write through a variable set by <c>while read</c> from a pipe: confined to the listed directories when the input is a file
    /// listing (<see cref="PipedFileListSource"/>), hidden otherwise. False when no such variable is used.
    /// </summary>
    private bool AddThroughInputVariables(IEnumerable<string> texts, string writer, CommandWrites visible, ShellLocations locations)
    {
        if (inputVariables.Count == 0)
        {
            return false;
        }

        List<string> used = [.. inputVariables.Keys.Where(name => texts.Any(text => VariableReference(name).IsMatch(text)))];
        if (used.Count == 0)
        {
            return false;
        }

        if (used.Any(name => inputVariables[name] is null))
        {
            writes.Hide($"while read {used[0]} … {writer}");
            return true;
        }

        writes.Add(visible with { Trees = [.. visible.Trees, .. used.SelectMany(name => inputVariables[name]!)] }, locations);
        return true;
    }

    private static Regex VariableReference(string name) => new($@"\$(\{{{Regex.Escape(name)}\}}|{Regex.Escape(name)}(?![A-Za-z0-9_]))", RegexOptions.CultureInvariant);

    /// <summary>A word that is nothing but expansions (<c>$X</c>, <c>"${CMD}"</c>, <c>$(…)</c>): its text is known only when it runs.</summary>
    private static bool IsExpansionOnly(ShellWord word) => !word.Source.LiteralDollar && ExpansionOnly().IsMatch(word.Text);

    [GeneratedRegex(@"^(\$(\{[^}]*\}|[A-Za-z_][A-Za-z0-9_]*|[0-9@*#?$!-]))+$")]
    private static partial Regex ExpansionOnly();

    /// <summary>A word is a command word, or the target of the redirection before it.</summary>
    private static void AddWord(ShellWord word, ref ShellTokenKind? redirect, List<ShellWord> segment)
    {
        if (redirect == ShellTokenKind.WriteRedirect)
        {
            segment.Add(word with { RedirectTarget = true });
        }
        else if (redirect is null)
        {
            segment.Add(word);
        }

        redirect = null;
    }

    private sealed record OpenGroup(
        ShellTokenKind Kind, List<ShellWord> Segment, ShellLocations Locations, ShellLiteralVariables Variables, string? Before, ShellTokenKind? Redirect);
}
