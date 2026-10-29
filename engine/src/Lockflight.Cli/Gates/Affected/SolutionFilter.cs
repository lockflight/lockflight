using System.Text.Json;
using Lockflight.Cli.Paths;

namespace Lockflight.Cli.Gates.Affected;

/// <summary>
/// Writes a solution filter (<c>.slnf</c>): the solution by its full path and the projects it keeps, as the solution lists them.
/// <c>dotnet build</c> and <c>dotnet test</c> run a filter as a solution of those projects; their project references still build.
/// The solution is named by its real path (symbolic links resolved, macOS <c>/var</c> → <c>/private/var</c>), as a build run in its
/// directory sees it: MSBuild's incremental clean compares output paths as strings, and under an alias it deletes the outputs a
/// full build wrote (a referenced project's reference assembly), failing the next build.
/// </summary>
internal static class SolutionFilter
{
    /// <summary>Where the filters live: git-ignored local state, one file per unit (<c>root</c> outside every unit).</summary>
    public const string RelativeDirectory = ".lockflight/local/affected";

    private const string RootName = "root";

    public static string PathFor(string projectRoot, string? unitPath) =>
        Path.Combine(projectRoot, RelativeDirectory.Replace('/', Path.DirectorySeparatorChar), $"{unitPath?.Replace('/', '-') ?? RootName}.slnf");

    public static async Task WriteAsync(string path, string solutionFile, IEnumerable<string> entries, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using FileStream stream = File.Create(path);
        await using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteStartObject("solution");
        writer.WriteString("path", Path.GetFullPath(RealPath.Resolve(solutionFile)));
        writer.WriteStartArray("projects");
        foreach (string entry in entries)
        {
            writer.WriteStringValue(entry);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
    }
}
