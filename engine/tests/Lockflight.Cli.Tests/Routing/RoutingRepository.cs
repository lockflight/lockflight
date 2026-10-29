using System.Text;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Routing;

/// <summary>
/// A temporary git repository for routing tests: base files committed on <c>main</c>, then a <c>change</c> branch where a test writes,
/// deletes and commits. Commits get a fixed identity and no signing; the user's diff configuration is left as git finds it unless a
/// test sets it.
/// </summary>
internal sealed class RoutingRepository : IDisposable
{
    public const string BaseBranch = "main";

    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-routing-");

    public string Root => directory.FullName;

    public static async Task<RoutingRepository> CreateAsync(IReadOnlyDictionary<string, string> baseFiles, CancellationToken cancellationToken)
    {
        RoutingRepository repository = new();
        await repository.GitAsync(["init", "-q", "-b", BaseBranch], cancellationToken);
        await repository.GitAsync(["config", "user.name", "Fixture"], cancellationToken);
        await repository.GitAsync(["config", "user.email", "fixture@example.com"], cancellationToken);
        await repository.GitAsync(["config", "commit.gpgsign", "false"], cancellationToken);
        await repository.GitAsync(["config", "core.autocrlf", "false"], cancellationToken);
        foreach ((string path, string content) in baseFiles)
        {
            repository.Write(path, content);
        }

        await repository.CommitAsync("base", cancellationToken);
        await repository.GitAsync(["checkout", "-q", "-b", "change"], cancellationToken);
        return repository;
    }

    public RoutingRepository Write(string relativePath, string content) => WriteBytes(relativePath, Encoding.UTF8.GetBytes(content));

    public RoutingRepository WriteBytes(string relativePath, byte[] content)
    {
        string path = Full(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return this;
    }

    public RoutingRepository Delete(string relativePath)
    {
        File.Delete(Full(relativePath));
        return this;
    }

    public RoutingRepository Move(string from, string to)
    {
        string target = Full(to);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(Full(from), target);
        return this;
    }

    public async Task<string> CommitAsync(string subject, CancellationToken cancellationToken)
    {
        await GitAsync(["add", "-A"], cancellationToken);
        await GitAsync(["commit", "-q", "--allow-empty", "-m", subject], cancellationToken);
        return (await GitAsync(["rev-parse", "HEAD"], cancellationToken)).Trim();
    }

    public async Task<string> GitAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ProcessResult result = await ProcessRunner.RunAsync("git", arguments, Root, cancellationToken);
        Assert.True(result.ExitCode == 0, $"git {string.Join(' ', arguments)}: {result.Combined}");
        return result.Output;
    }

    public void Dispose() => TempDirectories.DeleteRecursively(directory);

    private string Full(string relativePath) => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
