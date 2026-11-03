namespace Lockflight.Cli.Hooks;

/// <summary>
/// What a shell command visibly writes, with paths as written (relative ones to the hook's working directory). <see cref="Files"/>
/// are paths it writes or deletes; <see cref="Trees"/> are directories under which it may rewrite anything (a <c>git restore .</c>,
/// an <c>rm -r</c>); <see cref="SourceTrees"/> are directories under which it rewrites source files only (<c>dotnet format</c>).
/// A path may hold shell wildcards or variables; the guard judges those when it decides.
/// </summary>
internal sealed record ShellWrites(IReadOnlyList<string> Files, IReadOnlyList<string> Trees, IReadOnlyList<string> SourceTrees)
{
    public static ShellWrites None { get; } = new([], [], []);

    /// <summary>
    /// For each directory in <see cref="Trees"/> and <see cref="SourceTrees"/>, what to run instead when it can hold a protected file
    /// (<see cref="CommandWrites.TreeAdvice"/>); null when a command that rewrites it has no advice, or two commands that do disagree.
    /// </summary>
    public IReadOnlyDictionary<string, string?> TreeAdvice { get; init; } = new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <summary>A command reserved for a person found inside a nested command string (<c>bash -c "lockflight approve spec"</c>); null when none is.</summary>
    public string? HumanOnly { get; init; }

    /// <summary>As <see cref="HumanOnly"/>, with a request for help (<c>--help</c>, <c>-h</c>) counting like any other option.</summary>
    public string? HumanOnlyIgnoringHelp { get; init; }

    public static ShellWrites OfFile(string path) => None with { Files = [path] };
}
