namespace Lockflight.Cli.Tests.Schemas;

/// <summary>The published JSON Schemas and their fixtures in this checkout, found above the test binaries.</summary>
internal static class SchemaFiles
{
    public static string RepositoryRoot { get; } = Find();

    public static string Directory => Path.Combine(RepositoryRoot, "schemas");

    /// <summary>The fixtures of one contract id, by kind: <c>valid</c>, <c>invalid</c> or <c>engine-only</c>.</summary>
    public static IEnumerable<string> Fixtures(string id, string kind) =>
        System.IO.Directory.EnumerateFiles(Path.Combine(Directory, "fixtures", id, kind)).Order(StringComparer.Ordinal);

    private static string Find()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "lockflight.schema.json")))
            {
                return directory.FullName;
            }
        }

        throw new FileNotFoundException("lockflight.schema.json was not found above the test binaries");
    }
}
