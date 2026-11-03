using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>Compiles a sample into its own assembly on disk, so the host-tampering scan reads it alone or with the samples it names.</summary>
internal sealed class SnapshotSampleCompiler : IDisposable
{
    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(path => Path.GetFileName(path).StartsWith("System.", StringComparison.Ordinal) || Path.GetFileName(path) is "mscorlib.dll" or "netstandard.dll")
            .Select(path => MetadataReference.CreateFromFile(path)),
    ];

    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-snapshot-samples-");

    /// <summary>The path of the compiled assembly <paramref name="name"/>, built from <paramref name="source"/>, Debug or optimized.</summary>
    public string Compile(string name, string source, bool optimize = false, params string[] referenced)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source, path: Path.Combine(directory.FullName, name, "Sample.cs"))],
            [.. References, .. referenced.Select(path => MetadataReference.CreateFromFile(path))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: optimize ? OptimizationLevel.Release : OptimizationLevel.Debug, allowUnsafe: true));
        string path = Path.Combine(directory.FullName, $"{name}{(optimize ? ".release" : string.Empty)}.dll");
        EmitResult result = compilation.Emit(path);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        return path;
    }

    public void Dispose() => TempDirectories.DeleteRecursively(directory);
}
