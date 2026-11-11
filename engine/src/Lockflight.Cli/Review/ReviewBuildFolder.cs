using System.Security.Cryptography;
using System.Text;

namespace Lockflight.Cli.Review;

/// <summary>
/// The <c>bin/</c> and <c>obj/</c> subfolder one review builds its reproductions into. The first review of a project at a time
/// takes the shared <see cref="ReproductionBuild.OutputFolder"/>, kept between runs so the next review builds incrementally; it
/// holds a lock file (in the temporary directory, named after the project root) while it builds. A review that starts while
/// another one holds it gets a folder of its own, removed again when it is done, so two reviews never write the same output.
/// Fix checks build into <see cref="FixName"/> next to it, so a fixed build never leaves its production code in the reproductions' output.
/// </summary>
internal sealed class ReviewBuildFolder : IDisposable
{
    private const int RootHashBytes = 6;
    private const int OwnSuffixLength = 8;
    private const string FixSuffix = "-fix";

    private readonly FileStream? lockFile;
    private readonly string root;

    private ReviewBuildFolder(string root, string name, FileStream? lockFile)
    {
        this.root = root;
        Name = name;
        this.lockFile = lockFile;
    }

    public string Name { get; }

    public string FixName => $"{Name}{FixSuffix}";

    public static ReviewBuildFolder Acquire(string root)
    {
        string full = Path.GetFullPath(root);
        try
        {
            FileStream held = new(LockPath(full), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new ReviewBuildFolder(full, ReproductionBuild.OutputFolder, held);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Held by another review, or a file of that name another user of the machine made: build into a folder of our own.
            return new ReviewBuildFolder(full, $"{ReproductionBuild.OutputFolder}-{Guid.NewGuid().ToString("N")[..OwnSuffixLength]}", null);
        }
    }

    /// <summary>The lock file of a project root, in the temporary directory.</summary>
    public static string LockPath(string root)
    {
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant()))[..RootHashBytes]);
        return Path.Combine(Path.GetTempPath(), $"lockflight-review-{hash}.lock");
    }

    public void Dispose()
    {
        if (lockFile is not null)
        {
            lockFile.Dispose();
            return;
        }

        foreach (string project in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            string directory = Path.GetDirectoryName(project)!;
            foreach (string name in new[] { Name, FixName })
            {
                Delete(Path.Combine(directory, "bin", name));
                Delete(Path.Combine(directory, "obj", name));
            }
        }
    }

    private static void Delete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Still in use (a build server): it stays under bin/ or obj/, which builds ignore.
        }
    }
}
