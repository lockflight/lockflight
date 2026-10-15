using System.Runtime.CompilerServices;

namespace Lockflight.Cli.Hooks;

/// <summary>
/// Best-effort extraction of what a shell command line would write or delete: redirections in every fd form, the writers known to
/// <see cref="CommandWriters"/>, commands inside substitutions (<c>$( … )</c>, backticks), subshells, PowerShell script blocks, and
/// the command strings of other shells (<see cref="NestedShellCommand"/>). Relative paths are checked under every directory a
/// <c>cd</c> may have moved to (see <see cref="ShellLocations"/>). Targets computed at run time (substitutions, pipelines into
/// PowerShell writers, .NET file APIs) are reported as hidden. A guardrail against accidents, not a boundary: interpreters
/// (<c>python -c</c>, <c>node -e</c>), scripts and build tools are invisible to it.
/// </summary>
internal sealed class ShellWriteTargets(ShellWritesBuilder writes)
{
    private const int MaxNesting = 4;

    /// <summary>The last simple command seen: the left side of a pipe when the next one reads it.</summary>
    private ShellCommand? previous;

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
        // A variable the statement assigns is expanded in its redirections with the value from before it.
        bool assigns = segment.Any(word => word.Assignment is not null && !word.RedirectTarget);
        foreach (ShellWord target in segment.Where(word => word.RedirectTarget))
        {
            writes.AddFile(assigns ? target.Source.Text : target.Text, locations);
        }

        List<ShellWord> commandWords = [.. segment.Where(word => !word.RedirectTarget)];
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
            return;
        }

        ShellCommand command = parsed with { PipedInput = piped };
        ShellCommand? upstream = piped ? previous : null;
        previous = command;

        if (ShellLocations.Changers.Contains(command.Program))
        {
            locations.ChangeDirectory(command.Program, command.Arguments);
            return;
        }

        if (NestedShellCommand.Find(command) is NestedShellCommand nested)
        {
            if (nested.Hidden is not null || depth >= MaxNesting)
            {
                writes.Hide(nested.Hidden ?? $"{command.Program} nested {MaxNesting} levels deep");
                return;
            }

            Scan(nested.Command, nested.Dialect, nested.SameShell ? locations : locations.Copy(), depth + 1);
            return;
        }

        if (command.ArgumentsFromInput && upstream is not null && CommandWriters.IsWriter(command.Program, dialect)
            && PipedFileListSource.Directories(upstream) is IReadOnlyList<string> listed)
        {
            // grep -l … DIR | xargs sed -i …: the files come from under DIR, so the writer rewrites at most that tree.
            CommandWrites visible = CommandWriters.Of(command with { ArgumentsFromInput = false }, dialect);
            writes.Add(visible with { Trees = [.. visible.Trees, .. listed] }, locations);
            return;
        }

        writes.Add(CommandWriters.Of(command, dialect), locations);
    }

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
