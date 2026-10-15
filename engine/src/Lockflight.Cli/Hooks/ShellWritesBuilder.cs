namespace Lockflight.Cli.Hooks;

/// <summary>
/// Collects each command's writes under every directory the command may be running in. Brace lists are expanded. A target that
/// is entirely the output of a command substitution (<c>rm $(cat list)</c>) cannot be known and makes the command hidden; a
/// substitution inside literal text (<c>logs/$(date +%F).log</c>) stands for a <c>*</c> in that segment; one that starts the target
/// (<c>"$(pwd)/x"</c>) is also judged like a variable, by the literal text after it.
/// </summary>
internal sealed class ShellWritesBuilder
{
    private readonly List<string> files = [];
    private readonly List<string> trees = [];
    private readonly List<string> sourceTrees = [];
    private readonly List<string> patternFiles = [];
    private readonly List<string> computedFiles = [];
    private readonly List<string> floatingFiles = [];
    private readonly List<string> floatingTrees = [];
    private string? hidden;

    public void Add(CommandWrites writes, ShellLocations locations)
    {
        AddEach(writes.Files, locations, files, floatingFiles, computedFiles);
        AddEach(writes.Trees, locations, trees, floatingTrees, trees);
        AddEach(writes.SourceTrees, locations, sourceTrees, floatingTrees, sourceTrees);
        AddEach(writes.PatternFiles, locations, patternFiles, floatingFiles, patternFiles);
        hidden ??= writes.Hidden;
    }

    public void AddFile(string path, ShellLocations locations) => AddEach([path], locations, files, floatingFiles, computedFiles);

    public void Hide(string description) => hidden ??= description;

    public ShellWrites Build() =>
        new(Distinct(files), Distinct(trees), Distinct(sourceTrees), Distinct(floatingFiles), Distinct(floatingTrees), hidden)
        {
            PatternFiles = Distinct(patternFiles),
            ComputedFiles = Distinct(computedFiles),
        };

    /// <summary>Adds each path under every location; one with a substitution inside literal text goes to <paramref name="computed"/>.</summary>
    private void AddEach(IReadOnlyList<string> paths, ShellLocations locations, List<string> known, List<string> floating, List<string> computed)
    {
        foreach (string path in paths.Where(path => path.Length > 0))
        {
            string target = path;
            List<string> destination = known;
            if (path.Contains(ShellTokenizer.SubstitutionPlaceholder, StringComparison.Ordinal))
            {
                if (path.Replace(ShellTokenizer.SubstitutionPlaceholder, string.Empty, StringComparison.Ordinal).Trim('/', '\\', '.').Length == 0)
                {
                    Hide($"'{path.Replace(ShellTokenizer.SubstitutionPlaceholder, "$(…)", StringComparison.Ordinal)}' (a path computed by a command)");
                    continue;
                }

                // A leading substitution may also be an absolute directory: the target is then judged like "$VAR/rest" as well.
                if (path.StartsWith(ShellTokenizer.SubstitutionPlaceholder, StringComparison.Ordinal))
                {
                    known.Add(path);
                }

                target = path.Replace(ShellTokenizer.SubstitutionPlaceholder, "*", StringComparison.Ordinal);
                destination = computed;
            }

            if (BraceExpansion.Expand(target) is not IReadOnlyList<string> expanded)
            {
                Hide($"'{path}' (more than {BraceExpansion.MaxExpansions} brace expansions)");
                continue;
            }

            foreach ((string candidate, bool isFloating) in expanded.SelectMany(locations.Candidates))
            {
                (isFloating ? floating : destination).Add(candidate);
            }
        }
    }

    private static List<string> Distinct(List<string> paths) => [.. paths.Distinct(StringComparer.Ordinal)];
}
