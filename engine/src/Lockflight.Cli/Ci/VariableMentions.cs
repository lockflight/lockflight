using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Whether a snapshot variable is named anywhere in the repository outside one test project's own C# (<see cref="SnapshotRecords"/>):
/// in any file git lists (tracked, or untracked and not ignored), Markdown, other projects' sources, <c>bin</c>, <c>obj</c> and
/// <c>node_modules</c> included, since any of them could set it for the test run (a runsettings, an MSBuild import, launch settings,
/// a workflow, a helper). The files are read once, on the first question. A file that cannot be read, or a tree git cannot list,
/// counts as naming every variable.
/// </summary>
internal sealed class VariableMentions
{
    private const string ProjectExtension = ".csproj";
    private const string SourceExtension = ".cs";

    private readonly string root;
    private readonly IReadOnlyList<string>? files;
    private readonly IReadOnlyDictionary<string, string> projects;
    private readonly Dictionary<(string Name, string Assembly), bool> answers = [];
    private List<(string Path, string Text)>? texts;
    private bool unreadable;

    private VariableMentions(string root, IReadOnlyList<string>? files, IReadOnlyDictionary<string, string> projects)
    {
        this.root = Path.GetFullPath(root);
        this.files = files;
        this.projects = projects;
    }

    /// <summary>
    /// Lists the files under <paramref name="root"/> with git. <paramref name="projects"/> maps a test assembly's name to its project
    /// directory, whose own C# files may name the variables it reads.
    /// </summary>
    public static async Task<VariableMentions> ListAsync(string root, IReadOnlyDictionary<string, string> projects, CancellationToken cancellationToken)
    {
        ProcessResult listing = await ProcessRunner.RunAsync(
            "git", ["-c", "core.quotePath=false", "ls-files", "-z", "--cached", "--others", "--exclude-standard"], root, cancellationToken);
        IReadOnlyList<string>? files = listing.ExitCode == 0
            ? [.. listing.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal)]
            : null;
        return new VariableMentions(root, files, projects);
    }

    public bool IsNamedOutside(string name, string assembly)
    {
        if (!answers.TryGetValue((name, assembly), out bool named))
        {
            texts ??= Read();
            string? own = projects.GetValueOrDefault(assembly);
            answers[(name, assembly)] = named = unreadable || files is null
                || texts.Any(entry => entry.Text.Contains(name, StringComparison.OrdinalIgnoreCase) && !IsOwnSource(entry.Path, own));
        }

        return named;
    }

    private List<(string Path, string Text)> Read()
    {
        List<(string Path, string Text)> read = [];
        foreach (string file in files ?? [])
        {
            string path = Path.GetFullPath(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar)));
            try
            {
                read.Add((path, File.ReadAllText(path)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                unreadable = true;
            }
        }

        return read;
    }

    /// <summary>Whether the file is C# whose nearest project file is in <paramref name="projectDirectory"/>: the test project's own code.</summary>
    private bool IsOwnSource(string path, string? projectDirectory)
    {
        if (projectDirectory is null || !path.EndsWith(SourceExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string own = Path.GetFullPath(projectDirectory).TrimEnd(Path.DirectorySeparatorChar);
        for (string? directory = Path.GetDirectoryName(path); directory is not null && directory.Length >= root.Length; directory = Path.GetDirectoryName(directory))
        {
            if (Directory.EnumerateFiles(directory, "*" + ProjectExtension).Any())
            {
                return string.Equals(directory.TrimEnd(Path.DirectorySeparatorChar), own, StringComparison.Ordinal);
            }
        }

        return false;
    }
}
