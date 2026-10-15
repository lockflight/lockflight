using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Gates;

public sealed class SharedChangesTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-shared-");

    [Fact]
    public async Task OnlyBuildInputsOutsideEveryUnitCountAsSharedChanges()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Write(Manifest.FileName, "version: 1\nunits:\n  - path: svc/a\n");
        Write("libs/shared/Foo.cs", "class Foo {}\n");
        Write("svc/a/A.cs", "class A {}\n");
        Write("specs/001/spec.md", "spec\n");
        await GitAsync(["init", "-q"]);
        await GitAsync(["add", "."]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "base"]);
        Manifest manifest = ManifestLocator.Discover(root.FullName).Manifest!;

        string? clean = await SharedChanges.FingerprintAsync(manifest, cancellationToken);
        Write("specs/001/spec.md", "spec, edited\n");
        Write("svc/a/A.cs", "class A { int x; }\n");
        string? unitAndSpecOnly = await SharedChanges.FingerprintAsync(manifest, cancellationToken);
        Write("libs/shared/Foo.cs", "class Foo { int broken }\n");
        string? sharedLibrary = await SharedChanges.FingerprintAsync(manifest, cancellationToken);
        Write("Directory.Build.props", "<Project />\n");
        string? withNewBuildFile = await SharedChanges.FingerprintAsync(manifest, cancellationToken);

        Assert.Null(clean);
        Assert.Null(unitAndSpecOnly);
        Assert.NotNull(sharedLibrary);
        Assert.NotEqual(sharedLibrary, withNewBuildFile);
    }

    private void Write(string relative, string text)
    {
        string path = Path.Combine(root.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private async Task GitAsync(string[] arguments)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, root.FullName, TestContext.Current.CancellationToken);
        Assert.True(result.ExitCode == 0, result.Combined);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);
}
