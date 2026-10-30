using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Routing;

public sealed class RouteShowCommandTests
{
    private static readonly Dictionary<string, string> BaseFiles = new(StringComparer.Ordinal)
    {
        ["lockflight.yaml"] = "version: 2\nrouting:\n  preset: default\n  critical:\n    - \"src/Shop/Payments/**\"\n",
        ["src/Shop/Shop.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
        ["src/Shop/Payments/Refund.cs"] = "class Refund { }\n",
        ["src/Shop/Cart.cs"] = "class Cart { }\n",
    };

    [Fact]
    public async Task ItPrintsTheClassLineTheNeedsAndEverySignalForTheWorkingTree()
    {
        using RoutingRepository repository = await RoutingRepository.CreateAsync(BaseFiles, TestContext.Current.CancellationToken);
        repository.Write("src/Shop/Coupon.cs", "class Coupon { }\n");

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["route", "show", "--base", RoutingRepository.BaseBranch], string.Empty, repository.Root);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("local policy, not the base branch", output, StringComparison.Ordinal);
        Assert.Contains("**Class: behaviour**", output, StringComparison.Ordinal);
        Assert.Contains("Needs: gates", output, StringComparison.Ordinal);
        Assert.Contains("file.added → behaviour (heuristic) src/Shop/Coupon.cs", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACommitPairIsJudgedByTheBaseCommitsManifestNotTheHeads()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RoutingRepository repository = await RoutingRepository.CreateAsync(BaseFiles, cancellationToken);
        repository.Write("lockflight.yaml", "version: 2\nrouting:\n  preset: default\n  critical: []\n").Write("src/Shop/Payments/Refund.cs", "class Refund { int Amount; }\n");
        await repository.CommitAsync("loosen", cancellationToken);

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["route", "show", "--base", RoutingRepository.BaseBranch, "--head", "HEAD", "--json"], string.Empty, repository.Root);

        Assert.Equal(ExitCode.Pass, exitCode);
        Assert.Contains("\"class\": \"critical\"", output, StringComparison.Ordinal);
        Assert.Contains("\"id\": \"critical.path\"", output, StringComparison.Ordinal);
        Assert.Contains("\"id\": \"critical.policy\"", output, StringComparison.Ordinal);
        Assert.Contains("\"kind\": \"review\"", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExactMoveOutOfACriticalPathExitsWithAFailure()
    {
        using RoutingRepository repository = await RoutingRepository.CreateAsync(BaseFiles, TestContext.Current.CancellationToken);
        repository.Move("src/Shop/Payments/Refund.cs", "src/Shop/Refund.cs");

        (ExitCode exitCode, string output, _) = await TestConsole.RunAsync(["route", "show", "--base", RoutingRepository.BaseBranch], string.Empty, repository.Root);

        Assert.Equal(ExitCode.Fail, exitCode);
        Assert.Contains("FAIL routing.criticalMove: src/Shop/Payments/Refund.cs moved to src/Shop/Refund.cs", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownBaseIsAConfigurationErrorNeverAClass()
    {
        using RoutingRepository repository = await RoutingRepository.CreateAsync(BaseFiles, TestContext.Current.CancellationToken);

        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(["route", "show", "--base", "no-such-branch"], string.Empty, repository.Root);

        Assert.Equal(ExitCode.Config, exitCode);
        Assert.Empty(output);
        Assert.Contains("names no commit", error, StringComparison.Ordinal);
    }
}
