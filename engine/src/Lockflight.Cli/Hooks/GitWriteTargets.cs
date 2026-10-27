namespace Lockflight.Cli.Hooks;

/// <summary>
/// git subcommands that overwrite or delete working-tree files the agent did not name as edits: <c>checkout -- &lt;paths&gt;</c>,
/// <c>checkout -f</c>, <c>switch -f</c>, <c>restore</c>, <c>stash</c>, <c>reset --hard</c>, <c>clean -f</c>, <c>read-tree -u</c>,
/// <c>apply</c>, <c>rm</c>, <c>mv</c>, <c>checkout-index</c> and <c>archive -o</c> (the paths an archive is made of are only read). Pathspecs may be
/// files or directories, so each counts as both. Commands that only bring in committed states (<c>switch &lt;branch&gt;</c>,
/// <c>merge</c>, <c>pull</c>, <c>rebase</c>, <c>cherry-pick</c>, <c>am</c>) are not treated as writes: their content is reviewed
/// where it was committed.
/// </summary>
internal static class GitWriteTargets
{
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

        return new CommandWrites(Under(gitDirectory, writes.Files), Under(gitDirectory, writes.Trees), [], writes.Hidden);
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
            "read-tree" when arguments.Contains("-u") => [ShellArguments.CurrentDirectory],
            "apply" when Applies(arguments) => [ShellArguments.CurrentDirectory],
            "rm" when !arguments.Contains("--cached") => [.. afterSeparator, .. ShellArguments.Operands(beforeSeparator, [])],
            "mv" => [.. afterSeparator, .. ShellArguments.Operands(beforeSeparator, [])],
            "worktree" => WorktreePath(arguments),
            "archive" when ShellArguments.OptionValue(arguments, "-o", "--output") is string output => [output],
            "checkout-index" when arguments.Contains("--stdin") => [],
            "checkout-index" => CheckoutIndexPaths(beforeSeparator, afterSeparator),
            _ => [],
        };

        if (subcommand == "checkout-index" && arguments.Contains("--stdin"))
        {
            return CommandWrites.Hiding("git checkout-index --stdin");
        }

        if (subcommand == "mv" && paths.Count >= 2)
        {
            CommandWrites move = CopyCommandTargets.Of("mv", paths);
            return new CommandWrites(move.Files, [.. move.Trees, .. paths], [], null);
        }

        return CommandWrites.OfFilesAndTrees(paths);
    }

    /// <summary>
    /// <c>git checkout-index [-a] [--prefix=P] [--] [&lt;file&gt;…]</c> writes the named files, or every indexed file with <c>-a</c>;
    /// with a prefix they land under it instead (a tree: <c>out/</c>, or the directory a name prefix sits in).
    /// </summary>
    private static List<string> CheckoutIndexPaths(List<string> beforeSeparator, List<string> afterSeparator)
    {
        List<string> files = [.. ShellArguments.Operands(beforeSeparator, []), .. afterSeparator];
        string? prefix = beforeSeparator.FirstOrDefault(argument => argument.StartsWith("--prefix=", StringComparison.Ordinal))?["--prefix=".Length..];
        bool all = ShellArguments.HasFlag(beforeSeparator, "a", "--all");
        if (prefix is { Length: > 0 })
        {
            return [prefix.EndsWith('/') ? prefix.TrimEnd('/') : ShellArguments.Parent(prefix)];
        }

        return all ? [ShellArguments.CurrentDirectory] : files;
    }

    /// <summary><c>git worktree add [options] &lt;path&gt; [&lt;commit-ish&gt;]</c> checks a whole tree out at the path.</summary>
    private static List<string> WorktreePath(List<string> arguments)
    {
        if (arguments.Count == 0 || arguments[0] != "add")
        {
            return [];
        }

        List<string> operands = ShellArguments.Operands([.. arguments.Skip(1)], ["-b", "-B", "--reason"]);
        return operands.Count > 0 ? [operands[0]] : [];
    }

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

        int actionIndex = beforeSeparator.FindIndex(argument => !argument.StartsWith('-'));
        string? action = actionIndex >= 0 ? beforeSeparator[actionIndex] : null;
        if (action == "push" && ShellArguments.Operands([.. beforeSeparator.Skip(actionIndex + 1)], ["-m", "--message"]) is { Count: > 0 } pushed)
        {
            return pushed;
        }

        return action is null or "push" or "save" or "pop" or "apply" ? [ShellArguments.CurrentDirectory] : [];
    }

    private static List<string> Under(string directory, IReadOnlyList<string> paths) =>
        [.. paths.Select(path => path == ShellArguments.CurrentDirectory ? directory : ShellLocations.IsRooted(path) ? path : ShellArguments.Join(directory, path))];
}
