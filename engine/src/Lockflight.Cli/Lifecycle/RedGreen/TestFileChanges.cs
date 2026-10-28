namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>How the files of the locked test projects differ from their hashes at red, relative to the root and '/'-separated.</summary>
internal sealed record TestFileChanges(IReadOnlyList<string> Changed, IReadOnlyList<string> Added, IReadOnlyList<string> Deleted)
{
    public bool Any => Changed.Count > 0 || Added.Count > 0 || Deleted.Count > 0;

    /// <summary>"a.cs (changed), b.cs (added), c.cs (deleted)".</summary>
    public string Describe() =>
        string.Join(", ", Changed.Select(file => $"{file} (changed)").Concat(Added.Select(file => $"{file} (added)")).Concat(Deleted.Select(file => $"{file} (deleted)")));
}
