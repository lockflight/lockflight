using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Lifecycle.Fix;
using Lockflight.Cli.Lifecycle.RedGreen;
using Lockflight.Cli.Tests.Ci;

namespace Lockflight.Cli.Tests.Lifecycle.Fix;

public sealed class FeatureRedirectTests : IAsyncLifetime
{
    private static readonly EnvironmentReader Person = EnvironmentReader.Of(new Dictionary<string, string>());
    private static readonly EnvironmentReader Agent = EnvironmentReader.Of(new Dictionary<string, string> { [EnvironmentVariables.ClaudeCode] = "1" });

    private GitHistoryFixture repository = null!;

    public async ValueTask InitializeAsync()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        await repository.Write("lockflight.yaml", "version: 2\n").Write("specs/001-x/spec.md", "# Spec\n").CommitAsync("init", cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        repository.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ABranchWhoseFeatureHasARecordIsRefusedForAPersonToo()
    {
        await repository.CheckoutNewAsync("001-x", TestContext.Current.CancellationToken);
        Record(open: false).Save(Path.Combine(repository.Root, ".lockflight", "features", "001-x", RedGreenRecord.FileName), "001-x");

        Assert.Contains("has a red/green record", FeatureRedirect.Refusal(Manifest(), Person), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABranchWhoseFeatureHasASpecOnlyIsRefusedForAnAgentAndAllowedForAPerson()
    {
        await repository.CheckoutNewAsync("001-x", TestContext.Current.CancellationToken);

        Assert.Contains("/lockflight:feature", FeatureRedirect.Refusal(Manifest(), Agent), StringComparison.Ordinal);
        Assert.Null(FeatureRedirect.Refusal(Manifest(), Person));
    }

    [Fact]
    public async Task ASpecKitPointerNamingASpecRefusesAnAgentOnABranchThatNamesNone()
    {
        await repository.CheckoutNewAsync("fix/rounding", TestContext.Current.CancellationToken);
        repository.Write(".specify/feature.json", "{ \"feature_directory\": \"specs/001-x\" }\n");

        Assert.NotNull(FeatureRedirect.Refusal(Manifest(), Agent));
    }

    [Fact]
    public async Task TheOnlySpecIsNoFeatureForTheRedirect()
    {
        await repository.CheckoutNewAsync("fix/rounding", TestContext.Current.CancellationToken);

        Assert.Null(FeatureRedirect.Refusal(Manifest(), Agent));
    }

    [Fact]
    public async Task AnOpenCycleOfAnotherFeatureIsRefused()
    {
        await repository.CheckoutNewAsync("fix/rounding", TestContext.Current.CancellationToken);
        Record(open: true).Save(Path.Combine(repository.Root, ".lockflight", "features", "002-y", RedGreenRecord.FileName), "002-y");

        Assert.Contains("red cycle 1 of 002-y", FeatureRedirect.Refusal(Manifest(), Person), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreadableRecordIsRefusedNamingTheRecordAndTheResetCommand()
    {
        await repository.CheckoutNewAsync("fix/rounding", TestContext.Current.CancellationToken);
        repository.Write(".lockflight/features/002-y/redgreen.json", "not json");

        string? refusal = FeatureRedirect.Refusal(Manifest(), Person);

        Assert.Contains(".lockflight/features/002-y/redgreen.json", refusal, StringComparison.Ordinal);
        Assert.Contains("redgreen reset --feature 002-y", refusal, StringComparison.Ordinal);
    }

    private Manifest Manifest() => ManifestLocator.Discover(repository.Root).Manifest!;

    private static RedGreenRecord Record(bool open) => new()
    {
        BaselineCommit = new string('a', 40),
        BaselineAt = DateTimeOffset.UnixEpoch,
        BaselineTests = new HashSet<string>(StringComparer.Ordinal),
        TestProjects = ["tests/Shop.Tests"],
        Cycles = [new RedGreenCycle(1, DateTimeOffset.UnixEpoch, open ? null : DateTimeOffset.UnixEpoch, [])],
    };
}
