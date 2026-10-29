using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// A temporary git repository whose history a test scripts: files written, commits with trailers, branches, merges of the target,
/// rebases, cherry-picks, reverts and force-pushes by resetting a branch. Commits get a fixed identity and no signing.
/// </summary>
internal sealed class GitHistoryFixture : IDisposable
{
    private static readonly TimeSpan GitTimeLimit = TimeSpan.FromSeconds(60);

    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-ci-");

    public string Root => directory.FullName;

    public static async Task<GitHistoryFixture> CreateAsync(CancellationToken cancellationToken)
    {
        GitHistoryFixture fixture = new();
        await fixture.GitAsync(["init", "-q", "-b", "main"], cancellationToken);
        await fixture.GitAsync(["config", "user.name", "Fixture"], cancellationToken);
        await fixture.GitAsync(["config", "user.email", "fixture@example.com"], cancellationToken);
        await fixture.GitAsync(["config", "commit.gpgsign", "false"], cancellationToken);
        return fixture;
    }

    public GitHistoryFixture Write(string relativePath, string content)
    {
        string path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return this;
    }

    public void Delete(string relativePath) => File.Delete(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Commits everything with the subject, an optional body and optional trailers (each <c>Key: value</c>); the commit id.</summary>
    public async Task<string> CommitAsync(string subject, CancellationToken cancellationToken, string? body = null, params string[] trailers)
    {
        await GitAsync(["add", "-A"], cancellationToken);
        List<string> message = ["commit", "-q", "--allow-empty", "-m", subject];
        if (body is not null)
        {
            message.AddRange(["-m", body]);
        }

        if (trailers.Length > 0)
        {
            message.AddRange(["-m", string.Join('\n', trailers)]);
        }

        await GitAsync([.. message], cancellationToken);
        return await HeadAsync(cancellationToken);
    }

    public async Task<string> HeadAsync(CancellationToken cancellationToken) => (await GitAsync(["rev-parse", "HEAD"], cancellationToken)).Trim();

    public async Task<string> CommitOfAsync(string reference, CancellationToken cancellationToken) => (await GitAsync(["rev-parse", reference], cancellationToken)).Trim();

    public Task CheckoutNewAsync(string branch, CancellationToken cancellationToken) => GitAsync(["checkout", "-q", "-b", branch], cancellationToken);

    public Task CheckoutAsync(string branch, CancellationToken cancellationToken) => GitAsync(["checkout", "-q", branch], cancellationToken);

    /// <summary>Merges <paramref name="branch"/> into the current branch with a merge commit.</summary>
    public async Task<string> MergeAsync(string branch, CancellationToken cancellationToken)
    {
        await GitAsync(["merge", "-q", "--no-ff", "--no-edit", branch], cancellationToken);
        return await HeadAsync(cancellationToken);
    }

    public Task RebaseAsync(string onto, CancellationToken cancellationToken) => GitAsync(["rebase", "-q", onto], cancellationToken);

    public async Task<string> CherryPickAsync(string commit, CancellationToken cancellationToken)
    {
        await GitAsync(["cherry-pick", commit], cancellationToken);
        return await HeadAsync(cancellationToken);
    }

    public async Task<string> RevertAsync(string commit, CancellationToken cancellationToken)
    {
        await GitAsync(["revert", "--no-edit", commit], cancellationToken);
        return await HeadAsync(cancellationToken);
    }

    /// <summary>Moves the current branch to <paramref name="commit"/>, as a force-push after an amend or a dropped commit does.</summary>
    public Task ResetAsync(string commit, CancellationToken cancellationToken) => GitAsync(["reset", "-q", "--hard", commit], cancellationToken);

    public async Task<string> GitAsync(string[] arguments, CancellationToken cancellationToken)
    {
        ProcessResult git = await ProcessRunner.RunAsync("git", arguments, Root, GitTimeLimit, cancellationToken);
        Assert.True(git.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {git.Combined}");
        return git.Output;
    }

    public void Dispose() => TempDirectories.DeleteRecursively(directory);
}
