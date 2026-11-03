namespace Lockflight.Cli.Hooks;

/// <summary>
/// git subcommands that overwrite or delete working-tree files the agent did not name as edits: <c>checkout -- &lt;paths&gt;</c>,
/// <c>checkout -f</c>, <c>switch -f</c>, <c>restore</c>, <c>stash</c>, <c>reset --hard</c>, <c>clean -f</c>, <c>apply</c>, <c>am</c>,
/// <c>rm</c> and <c>mv</c>. Pathspecs may be files or directories, so each counts as both; a patch (<c>apply</c>, <c>am</c>) may
/// touch anything, so it counts as the whole working tree. Commands that only bring in committed states
/// (<c>switch &lt;branch&gt;</c>, <c>merge</c>, <c>pull</c>, <c>rebase</c>, <c>cherry-pick</c>) are not treated as writes: their
/// content is reviewed where it was committed.
/// </summary>
internal static class GitWriteTargets
{
    private const string ResetAdvice =
        "To undo a commit that changed no protected file, run 'git revert <commit>'; to take files back from a commit, name them: 'git restore --source <commit> --staged --worktree -- <files>'.";

    private const string RestoreAdvice = "Name the files instead: 'git restore --source <commit> --staged --worktree -- <files>'.";
    private const string UnstageAdvice = "Name the files instead: 'git restore --staged -- <files>'.";
    private const string SwitchAdvice = "Discard the changes by name first, 'git restore --source HEAD --staged --worktree -- <files>', then switch without -f.";
    private const string CleanAdvice = "Name the files to delete instead: 'git clean -f -- <files>'.";
    private const string StashAdvice = "Stash the files by name instead: 'git stash push --include-untracked -- <files>'.";

    private const string UnstashAdvice =
        "Take the files from the stash by name instead: 'git checkout \"stash@{0}\" -- <files>' for changed files, 'git checkout \"stash@{0}^3\" -- <files>' "
        + "for files that were untracked (both stage them: 'git restore --staged -- <files>' unstages), then check 'git status' before 'git stash drop'.";

    private static readonly HashSet<string> GitOptionsWithValue = ["-C", "-c", "--git-dir", "--work-tree", "--namespace"];
    private static readonly HashSet<string> ApplyReadOnlyFlags = ["--check", "--stat", "--numstat", "--summary"];

    public static CommandWrites Of(IReadOnlyList<string> arguments)
    {
        int index = 0;
        string? gitDirectory = null;
        while (index < arguments.Count && arguments[index].StartsWith('-'))
        {
            if (arguments[index] == "-C" && index + 1 < arguments.Count)
            {
                gitDirectory = gitDirectory is null ? arguments[index + 1] : ShellArguments.Join(gitDirectory, arguments[index + 1]);
            }

            index += GitOptionsWithValue.Contains(arguments[index]) ? 2 : 1;
        }

        if (index >= arguments.Count)
        {
            return CommandWrites.None;
        }

        CommandWrites writes = Subcommand(arguments[index], [.. arguments.Skip(index + 1)]);
        if (gitDirectory is null)
        {
            return writes;
        }

        return writes with { Files = Under(gitDirectory, writes.Files), Trees = Under(gitDirectory, writes.Trees), SourceTrees = [] };
    }

    private static CommandWrites Subcommand(string subcommand, List<string> arguments)
    {
        int separator = arguments.IndexOf(ShellArguments.EndOfOptions);
        List<string> afterSeparator = separator >= 0 ? [.. arguments.Skip(separator + 1)] : [];
        List<string> beforeSeparator = separator >= 0 ? [.. arguments.Take(separator)] : arguments;
        bool forced = ShellArguments.HasFlag(beforeSeparator, "f", "--force");

        List<string> paths = subcommand switch
        {
            "checkout" when separator >= 0 => afterSeparator,
            "checkout" => [.. CheckoutPaths(beforeSeparator), .. forced ? [ShellArguments.CurrentDirectory] : Array.Empty<string>()],
            "switch" when forced || beforeSeparator.Contains("--discard-changes") => [ShellArguments.CurrentDirectory],
            "restore" => [.. afterSeparator, .. ShellArguments.Operands(beforeSeparator, ["-s", "--source"])],
            "stash" => StashPaths(beforeSeparator, afterSeparator, separator >= 0),
            "reset" when arguments.Contains("--hard") || arguments.Contains("--merge") || arguments.Contains("--keep") => [ShellArguments.CurrentDirectory],
            "clean" when forced => afterSeparator.Count > 0 ? afterSeparator : [ShellArguments.CurrentDirectory],
            "apply" when Applies(arguments) => [ShellArguments.CurrentDirectory],
            "am" when !arguments.Any(argument => argument is "--show-current-patch" or "--quit") => [ShellArguments.CurrentDirectory],
            "rm" when !arguments.Contains("--cached") => [.. afterSeparator, .. ShellArguments.Operands(beforeSeparator, [])],
            "mv" => [.. afterSeparator, .. ShellArguments.Operands(beforeSeparator, [])],
            _ => [],
        };

        if (subcommand == "mv" && paths.Count >= 2)
        {
            CommandWrites move = CopyCommandTargets.Of("mv", paths);
            return new CommandWrites(move.Files, [.. move.Trees, .. paths], []);
        }

        return CommandWrites.OfFilesAndTrees(paths) with { TreeAdvice = Advice(subcommand, beforeSeparator, separator >= 0, forced) };
    }

    /// <summary>The command naming files to suggest when a directory the subcommand rewrites can hold a protected file.</summary>
    private static string? Advice(string subcommand, List<string> beforeSeparator, bool hasSeparator, bool forced) => subcommand switch
    {
        "reset" => ResetAdvice,
        "restore" when beforeSeparator.Any(argument => argument is "--staged" or "-S") && !beforeSeparator.Any(argument => argument is "--worktree" or "-W") => UnstageAdvice,
        "switch" => SwitchAdvice,
        "checkout" when forced && !hasSeparator => SwitchAdvice,
        "checkout" or "restore" => RestoreAdvice,
        "clean" => CleanAdvice,
        "stash" when StashAction(beforeSeparator) is "pop" or "apply" => UnstashAdvice,
        "stash" => StashAdvice,
        _ => null,
    };

    /// <summary><c>git apply</c> writes the working tree unless it only reports, or applies to the index alone.</summary>
    private static bool Applies(List<string> arguments) =>
        (arguments.Contains("--apply") || !arguments.Any(ApplyReadOnlyFlags.Contains))
        && (!arguments.Contains("--cached") || arguments.Contains("--index"));

    /// <summary>
    /// <c>git checkout &lt;tree-ish&gt; &lt;paths&gt;</c> without <c>--</c>. A lone operand may be a branch or a path (git prefers the
    /// branch only when both exist), so it counts as a path: a branch name that is no path in the working tree matches no file and
    /// is allowed; one that is a directory (<c>test</c>, <c>.</c>) is checked as a tree. A new branch (<c>-b</c>) writes nothing.
    /// </summary>
    private static List<string> CheckoutPaths(List<string> arguments)
    {
        if (arguments.Any(argument => argument is "-b" or "-B" or "--orphan"))
        {
            return [];
        }

        List<string> operands = ShellArguments.Operands(arguments, ["--conflict", "--pathspec-from-file"]);
        // "-" (the previous branch), revision syntax (HEAD~1, main^, @{u}) and a computed lone operand ("$BRANCH") are revisions.
        operands.RemoveAll(operand => operand == "-" || (operands.Count == 1 && operand.IndexOfAny(['~', '^', '@', '$']) >= 0));
        return operands.Count > 1 ? [.. operands.Skip(1)] : operands;
    }

    /// <summary>
    /// <c>git stash [push] [-- &lt;paths&gt;]</c> resets the stashed paths (all changes without paths); <c>pop</c>/<c>apply</c> write the
    /// stash back. <c>list</c>, <c>show</c> and the like write nothing.
    /// </summary>
    private static List<string> StashPaths(List<string> beforeSeparator, List<string> afterSeparator, bool hasSeparator)
    {
        if (hasSeparator)
        {
            return afterSeparator.Count > 0 ? afterSeparator : [ShellArguments.CurrentDirectory];
        }

        string? action = StashAction(beforeSeparator);
        if (action == "push" && ShellArguments.Operands([.. beforeSeparator.Skip(beforeSeparator.IndexOf(action) + 1)], ["-m", "--message"]) is { Count: > 0 } pushed)
        {
            return pushed;
        }

        return action is null or "push" or "save" or "pop" or "apply" ? [ShellArguments.CurrentDirectory] : [];
    }

    /// <summary>The stash subcommand (<c>push</c>, <c>pop</c>, …): the first word that is not an option; null for a bare <c>git stash</c>.</summary>
    private static string? StashAction(List<string> beforeSeparator) => beforeSeparator.FirstOrDefault(argument => !argument.StartsWith('-'));

    private static List<string> Under(string directory, IReadOnlyList<string> paths) =>
        [.. paths.Select(path => path == ShellArguments.CurrentDirectory ? directory : IsRooted(path) ? path : ShellArguments.Join(directory, path))];

    private static bool IsRooted(string path) =>
        path.StartsWith('/') || path.StartsWith('\\') || path.StartsWith('~') || path.StartsWith('$') || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':');
}
