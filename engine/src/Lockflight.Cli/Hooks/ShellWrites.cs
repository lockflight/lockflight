namespace Lockflight.Cli.Hooks;

/// <summary>
/// What a shell command visibly writes. <see cref="Files"/> are paths it writes or deletes; <see cref="Trees"/> are directories
/// under which it may rewrite anything (a <c>git restore .</c>, an <c>rm -r</c>); <see cref="SourceTrees"/> are directories under
/// which it rewrites source files only (<c>dotnet format</c>). Each path appears as written and, after a <c>cd</c>, also under the
/// directory the cd may have moved to; relative ones still need the hook's working directory. <see cref="FloatingFiles"/> and
/// <see cref="FloatingTrees"/> are '/'-separated paths below a directory a <c>cd</c> moved to that cannot be known (they may lie
/// anywhere). <see cref="Hidden"/> names a writer whose targets the command line does not show (<c>xargs rm</c>, a path computed by
/// a command substitution).
/// </summary>
internal sealed record ShellWrites(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Trees,
    IReadOnlyList<string> SourceTrees,
    IReadOnlyList<string> FloatingFiles,
    IReadOnlyList<string> FloatingTrees,
    string? Hidden)
{
    public static ShellWrites None { get; } = new([], [], [], [], [], null);

    /// <summary>Paths of the form <c>DIR/**/NAME-PATTERN</c> (see <see cref="CommandWrites.PatternFiles"/>).</summary>
    public IReadOnlyList<string> PatternFiles { get; init; } = [];

    /// <summary>Files whose name holds a <c>*</c> for the output of a command (<c>logs/*.log</c>); judged by pattern, not by what exists.</summary>
    public IReadOnlyList<string> ComputedFiles { get; init; } = [];

    /// <summary>A command reserved for a person found inside a nested command string (<c>approve spec</c>); null when none is.</summary>
    public string? HumanOnly { get; init; }

    /// <summary>As <see cref="HumanOnly"/>, with a request for help (<c>--help</c>, <c>-h</c>) counting like any other option.</summary>
    public string? HumanOnlyIgnoringHelp { get; init; }

    /// <summary>Whether the command writes anything the guard sees, or hides what it writes.</summary>
    public bool HasTargets =>
        Hidden is not null || Files.Count + Trees.Count + SourceTrees.Count + FloatingFiles.Count + FloatingTrees.Count + PatternFiles.Count + ComputedFiles.Count > 0;

    public static ShellWrites OfFile(string path) => None with { Files = [path] };
}
