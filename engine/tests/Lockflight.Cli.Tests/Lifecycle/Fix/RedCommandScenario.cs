using Lockflight.Cli.Hosting;
using Lockflight.Cli.Tests.Ci;

namespace Lockflight.Cli.Tests.Lifecycle.Fix;

/// <summary>
/// A repository whose main branch holds a bug (a price total rounded down, not to the nearest unit) and a fix branch cut from it:
/// <c>lockflight red</c> runs on it in-process, with a real build in its kept worktree.
/// </summary>
public abstract class RedCommandScenario : IAsyncLifetime
{
    private protected const string Buggy = "    public decimal Total(decimal net) => Math.Floor(net);";
    private protected const string Fixed = "    public decimal Total(decimal net) => Math.Round(net, MidpointRounding.AwayFromZero);";
    private protected const string RoundingTest = """
            [Fact]
            public void TheTotalIsRoundedToTheNearestUnit() => Assert.Equal(3m, new Price().Total(2.6m));
        """;

    private protected const string Branch = "fix/rounding";

    private protected GitHistoryFixture repository = null!;
    private protected string main = string.Empty;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        main = await repository.WriteSolution(Buggy, tests: string.Empty).Write("lockflight.yaml", ManifestText).CommitAsync("init", cancellationToken);
        await repository.CheckoutNewAsync(Branch, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>The target branch's manifest.</summary>
    private protected virtual string ManifestText => "version: 2\n";

    private protected Task<(ExitCode ExitCode, string Output, string Error)> RedAsync(params string[] options) =>
        TestConsole.RunAsync(["red", .. options], string.Empty, repository.Root, new Dictionary<string, string>());

    private protected string Ledger()
    {
        string directory = Path.Combine(repository.Root, ".lockflight", "ledger");
        return Directory.Exists(directory) ? string.Concat(Directory.EnumerateFiles(directory).Select(File.ReadAllText)) : string.Empty;
    }

    private protected async Task<string> GitOutputAsync(params string[] arguments) => (await repository.GitAsync(arguments, TestContext.Current.CancellationToken)).Trim();
}
