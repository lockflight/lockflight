namespace Lockflight.Cli.Tests.Schemas;

/// <summary>
/// What a released engine wrote, one folder per version under <c>schemas/fixtures/released/</c>, made by its
/// <c>generate.sh</c> with that version's published engine.
/// </summary>
internal static class ReleasedFixtures
{
    public static string Directory => Path.Combine(SchemaFiles.Directory, "fixtures", "released");

    /// <summary>Every released version with fixtures, oldest first.</summary>
    public static IReadOnlyList<string> Versions { get; } =
    [
        .. System.IO.Directory.EnumerateDirectories(Directory).Select(path => Path.GetFileName(path))
            .OrderBy(version => Version.Parse(version)),
    ];

    public static TheoryData<string> All() => [.. Versions];

    public static string PathOf(string version, string relativePath) =>
        Path.Combine(Directory, version, relativePath.Replace('/', Path.DirectorySeparatorChar));

    public static string Read(string version, string relativePath) => File.ReadAllText(PathOf(version, relativePath));

    /// <summary>The files of one folder of a version's fixtures, by name.</summary>
    public static IEnumerable<string> Files(string version, string folder) =>
        System.IO.Directory.EnumerateFiles(PathOf(version, folder)).Order(StringComparer.Ordinal);
}
