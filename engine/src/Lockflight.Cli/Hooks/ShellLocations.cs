namespace Lockflight.Cli.Hooks;

/// <summary>
/// The directories a command may be running in at a point of a command line. A <c>cd</c> only adds to the set — it may fail, run
/// in a pipeline (zsh keeps the last stage's cd) or be undone by <c>popd</c> — so a guess can add a path to check but never hide
/// the path as written. A cd the scanner cannot follow adds <see cref="ShellLocation.Unknown"/>: relative paths after it are
/// checked as if they could lie anywhere.
/// </summary>
internal sealed class ShellLocations(ShellDialect dialect)
{
    private const int MaxLocations = 32;
    private const string PreviousDirectory = "-";

    private static readonly HashSet<string> PushCommands = ["pushd", "push-location"];
    private static readonly HashSet<string> PopCommands = ["popd", "pop-location"];
    private static readonly HashSet<string> CdOptionsWithValue = ["-stackname"];

    private HashSet<ShellLocation> locations = [ShellLocation.Start];
    private int pushes;
    private bool moved;

    /// <summary>Commands that change the directory of the commands after them.</summary>
    public static IReadOnlySet<string> Changers { get; } =
        new HashSet<string>(["cd", "chdir", "pushd", "popd", "set-location", "sl", "push-location", "pop-location"]);

    public ShellLocations Copy() => new(dialect) { locations = [.. locations], pushes = pushes, moved = moved };

    public void ChangeDirectory(string program, IReadOnlyList<string> arguments)
    {
        if (PopCommands.Contains(program))
        {
            // Back to a directory already in the set when this command line pushed it; otherwise to one pushed earlier.
            if (pushes > 0)
            {
                pushes--;
            }
            else
            {
                Add(ShellLocation.Unknown);
            }

            return;
        }

        pushes += PushCommands.Contains(program) ? 1 : 0;
        string? target = TargetOf(arguments);
        if (target == PreviousDirectory && moved)
        {
            return;
        }

        if (target is null || !IsPlainDirectory(target))
        {
            Add(ShellLocation.Unknown);
        }
        else
        {
            foreach (ShellLocation location in locations.ToList())
            {
                Add(Resolve(location, target));
            }
        }

        moved = true;
    }

    /// <summary>The paths a target may mean, one per location; floating ones are '/'-separated and relative to an unknown directory.</summary>
    public IEnumerable<(string Path, bool Floating)> Candidates(string path)
    {
        if (IsRooted(path))
        {
            yield return (path, false);
            yield break;
        }

        foreach (ShellLocation location in locations)
        {
            yield return location switch
            {
                { Floating: true } => (NormalizeFloating(Join(location.Directory!, path)), true),
                { Directory: null } => (path, false),
                _ => (Join(location.Directory, path), false),
            };
        }
    }

    /// <summary>
    /// Absolute, drive and home paths do not depend on the directory a <c>cd</c> moved to; neither does a path that starts with a
    /// variable (<c>$PWD/x</c>, <c>$TMPDIR/x</c>), which the guard resolves or judges by its literal end.
    /// </summary>
    public static bool IsRooted(string path) =>
        path.StartsWith('/') || path.StartsWith('\\') || path.StartsWith('~') || path.StartsWith('$') || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':');

    private static string? TargetOf(IReadOnlyList<string> arguments)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (argument.StartsWith("-path:", StringComparison.OrdinalIgnoreCase) || argument.StartsWith("-literalpath:", StringComparison.OrdinalIgnoreCase))
            {
                return argument[(argument.IndexOf(':', StringComparison.Ordinal) + 1)..];
            }

            if (CdOptionsWithValue.Contains(argument.ToLowerInvariant()))
            {
                index++;
            }
            else if (argument == PreviousDirectory || !argument.StartsWith('-'))
            {
                return argument;
            }
        }

        return null;
    }

    /// <summary>A cd target whose meaning is plain text: no expansion, no home, no glob, no "previous directory".</summary>
    private static bool IsPlainDirectory(string target) => target != PreviousDirectory && target.IndexOfAny(['$', '`', '~', '*', '?', '[']) < 0;

    private void Add(ShellLocation location)
    {
        if (locations.Count >= MaxLocations)
        {
            locations.Add(ShellLocation.Unknown);
            return;
        }

        locations.Add(location);
    }

    private ShellLocation Resolve(ShellLocation location, string target)
    {
        if (IsRooted(target))
        {
            return new ShellLocation(target, Floating: false);
        }

        if (location.Floating)
        {
            return new ShellLocation(NormalizeFloating(Join(location.Directory!, target)), Floating: true);
        }

        return new ShellLocation(location.Directory is null ? target : Join(location.Directory, target), Floating: false);
    }

    private string Join(string directory, string path)
    {
        if (directory.Length == 0 || path == ".")
        {
            return directory.Length == 0 ? path : directory;
        }

        char separator = dialect == ShellDialect.PowerShell ? '\\' : '/';
        return $"{directory.TrimEnd('/', '\\')}{separator}{path}";
    }

    /// <summary>'/'-separated with <c>.</c> and <c>..</c> collapsed; a <c>..</c> above the unknown directory stays unknown.</summary>
    private static string NormalizeFloating(string path)
    {
        List<string> segments = [];
        foreach (string segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }
            }
            else if (segment != ".")
            {
                segments.Add(segment);
            }
        }

        return string.Join('/', segments);
    }
}
