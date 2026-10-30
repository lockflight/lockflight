using Lockflight.Cli.Ci;

namespace Lockflight.Cli.Tests.Ci;

public sealed class PolicyOverlayTests : IAsyncLifetime
{
    private GitHistoryFixture repository = null!;

    public async ValueTask InitializeAsync() => repository = await GitHistoryFixture.CreateAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task TheBasesPolicyFilesBaselinesAndStyleConfigReplaceTheTrees()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string policy = await repository.Write("lockflight.yaml", "version: 1\n").Write(".lockflight/baselines/tests.json", "base\n").Write("src/.editorconfig", "[*]\nseverity = error\n")
            .CommitAsync("base policy", cancellationToken);
        string tree = await repository.Write("lockflight.yaml", "version: 1\nci:\n  maxRedCommits: 50\n").Write(".lockflight/baselines/tests.json", "accepted my failures\n")
            .Write("src/.editorconfig", "[*]\nseverity = none\n").Write("src/loose.globalconfig", "is_global = true\n").Write("nuget.config", "<configuration />\n")
            .CommitAsync("pull request loosens it", cancellationToken);
        using GitHistoryFixture target = await GitHistoryFixture.CreateAsync(cancellationToken);
        await repository.GitAsync(["worktree", "add", "--detach", "-f", Path.Combine(target.Root, "w"), tree], cancellationToken);
        string worktree = Path.Combine(target.Root, "w");

        string? error = await PolicyOverlay.ApplyAsync(repository.Root, worktree, tree, policy, TimeSpan.FromSeconds(60), cancellationToken);

        Assert.Null(error);
        Assert.Equal("version: 1\n", File.ReadAllText(Path.Combine(worktree, "lockflight.yaml")));
        Assert.Equal("base\n", File.ReadAllText(Path.Combine(worktree, ".lockflight", "baselines", "tests.json")));
        Assert.Equal("[*]\nseverity = error\n", File.ReadAllText(Path.Combine(worktree, "src", ".editorconfig")));
        Assert.False(File.Exists(Path.Combine(worktree, "src", "loose.globalconfig")));
        Assert.False(File.Exists(Path.Combine(worktree, "nuget.config")));
    }

    [Fact]
    public async Task ACaseVariantOfTheBasesNuGetConfigNeverSurvives()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string policy = await repository.Write("NuGet.config", "<configuration><!-- base --></configuration>\n").CommitAsync("base policy", cancellationToken);
        repository.Write("variant.tmp", "<configuration><!-- pull request --></configuration>\n");
        string blob = (await repository.GitAsync(["hash-object", "-w", "variant.tmp"], cancellationToken)).Trim();
        repository.Delete("variant.tmp");
        await repository.GitAsync(["update-index", "--add", "--cacheinfo", $"100644,{blob},NuGet.Config"], cancellationToken);
        await repository.GitAsync(["commit", "-q", "-m", "pull request adds a variant"], cancellationToken);
        string tree = await repository.HeadAsync(cancellationToken);
        using GitHistoryFixture target = await GitHistoryFixture.CreateAsync(cancellationToken);
        await repository.GitAsync(["worktree", "add", "--detach", "-f", Path.Combine(target.Root, "w"), tree], cancellationToken);
        string worktree = Path.Combine(target.Root, "w");

        Assert.Null(await PolicyOverlay.ApplyAsync(repository.Root, worktree, tree, policy, TimeSpan.FromSeconds(60), cancellationToken));

        string[] configs = [.. Directory.EnumerateFiles(worktree).Where(path => Path.GetFileName(path).Equals("nuget.config", StringComparison.OrdinalIgnoreCase))];
        Assert.NotEmpty(configs);
        Assert.All(configs, path => Assert.Contains("base", File.ReadAllText(path), StringComparison.Ordinal));
    }
}
