namespace Lockflight.Cli.Ci;

/// <summary>
/// Whether a snapshot variable is named anywhere in the tree outside the test code (<see cref="SnapshotRecords"/>): in any file
/// but a C# source of a project that is not production code (a runsettings, MSBuild, launch settings or workflow file, production
/// code), each of which could set it for the test run. Markdown is left out. The tree is read once, on the first question, and
/// <c>.git</c>, <c>bin</c> and <c>obj</c> are skipped. A file or folder that cannot be read, or a linked folder, counts as naming
/// every variable: a tree the scan cannot read never allows a record-mode write.
/// </summary>
internal sealed class VariableMentions(string root, IReadOnlySet<string> productionProjects)
{
    private static readonly string[] SkippedDirectories = [".git", "bin", "obj", "node_modules"];

    private readonly Dictionary<string, bool> answers = new(StringComparer.OrdinalIgnoreCase);
    private List<string>? texts;
    private bool unreadable;

    public bool IsNamedOutsideTheTests(string name)
    {
        if (!answers.TryGetValue(name, out bool named))
        {
            texts ??= Read();
            answers[name] = named = unreadable || texts.Any(text => text.Contains(name, StringComparison.OrdinalIgnoreCase));
        }

        return named;
    }

    private List<string> Read()
    {
        List<string> read = [];
        try
        {
            foreach (string file in Files(root))
            {
                if (!file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !IsTestSource(file))
                {
                    read.Add(File.ReadAllText(file));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or LinkedFolderException)
        {
            unreadable = true;
        }

        return read;
    }

    /// <summary>Whether the file is C# of a project that is not production code: the nearest project file above it is not one.</summary>
    private bool IsTestSource(string file)
    {
        if (!file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (string? directory = Path.GetDirectoryName(file); directory is not null && directory.Length >= root.Length; directory = Path.GetDirectoryName(directory))
        {
            string[] projects = Directory.GetFiles(directory, "*.csproj");
            if (projects.Length > 0)
            {
                return projects.All(project => !productionProjects.Contains(Path.GetFileNameWithoutExtension(project)));
            }
        }

        return false;
    }

    /// <summary>A linked folder was found: it could lead outside the tree or around in a loop, so the tree is not read.</summary>
    private sealed class LinkedFolderException : Exception;

    private static IEnumerable<string> Files(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            yield return file;
        }

        foreach (string child in Directory.EnumerateDirectories(directory).Where(child => !SkippedDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase)))
        {
            if (new DirectoryInfo(child).LinkTarget is not null)
            {
                throw new LinkedFolderException();
            }

            foreach (string file in Files(child))
            {
                yield return file;
            }
        }
    }
}
