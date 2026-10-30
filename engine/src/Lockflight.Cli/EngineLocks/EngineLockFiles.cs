using Lockflight.Cli.Config;

namespace Lockflight.Cli.EngineLocks;

/// <summary>
/// The lock that pins a project's engine for this clone: the git-ignored <see cref="EngineLock.LocalRelativePath"/> of a
/// development build when there is one, else the committed <see cref="EngineLock.RelativePath"/>. CI reads only the committed one.
/// </summary>
internal static class EngineLockFiles
{
    public static string CommittedPath(string root) => Path.Combine(root, EngineLock.RelativePath.Replace('/', Path.DirectorySeparatorChar));

    public static string LocalPath(string root) => Path.Combine(root, EngineLock.LocalRelativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The lock that applies here; a lock that cannot be read is the problem, never "no lock".</summary>
    public static EngineLockReading ReadForClone(string root)
    {
        EngineLockReading local = EngineLockReader.ReadFile(LocalPath(root), allowDevelopment: true, ToolPin.RunningVersion);
        return local.Lock is not null || local.Problem is not null ? local : ReadCommitted(root);
    }

    public static EngineLockReading ReadCommitted(string root) => EngineLockReader.ReadFile(CommittedPath(root), allowDevelopment: false, ToolPin.RunningVersion);

    /// <summary>Writes the lock's canonical text through a temporary file.</summary>
    public static void Write(string path, EngineLock engineLock)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, EngineLockText.Write(engineLock));
        File.Move(temporary, path, overwrite: true);
    }
}
