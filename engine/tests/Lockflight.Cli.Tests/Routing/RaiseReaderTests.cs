using Lockflight.Cli.Config;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Tests.Routing;

public sealed class RaiseReaderTests
{
    private const string Spec = "# Feature: Refunds\n\n**Status**: Draft\n**Class**: critical\n\n## User Scenarios\n";

    private static readonly Dictionary<string, string> BaseFiles = new(StringComparer.Ordinal)
    {
        ["src/Shop/Shop.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
        ["src/Shop/Cart.cs"] = "class Cart { }\n",
        ["specs/003-cart/spec.md"] = "# Feature: Cart\n\n**Class**: feature\n",
    };

    [Fact]
    public async Task AChangedSpecsClassLineRaises()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RoutingRepository repository = await RoutingRepository.CreateAsync(BaseFiles, cancellationToken);
        repository.Write("specs/004-refunds/spec.md", Spec);
        await repository.CommitAsync("spec", cancellationToken);

        RaiseReading reading = await ReadAsync(repository, RaiseInputs.None, cancellationToken);

        Assert.Equal([new ClassRaise("spec 004-refunds", ChangeClass.Critical)], reading.Raises);
    }

    [Theory]
    [InlineData("003-cart")]
    [InlineData("feat/003-cart")]
    [InlineData("003-cart-totals")]
    public async Task TheFeatureTheBranchNamesIsCoveringThoughItsSpecIsUnchanged(string branch)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RoutingRepository repository = await RoutingRepository.CreateAsync(BaseFiles, cancellationToken);
        repository.Write("src/Shop/Cart.cs", "class Cart { int Total; }\n");
        await repository.CommitAsync("code", cancellationToken);

        RaiseReading committed = await ReadAsync(repository, new RaiseInputs(branch, [], Raise: null), cancellationToken);
        RaiseReading local = await ReadAsync(repository, new RaiseInputs(branch, [], Raise: null), cancellationToken, headRef: null);

        Assert.Equal([new ClassRaise("spec 003-cart", ChangeClass.Feature)], committed.Raises);
        Assert.Equal(committed.Raises, local.Raises);
    }

    [Fact]
    public async Task AFeaturesRecordedClassOnTheBaseRaisesItsLaterPullRequests()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Dictionary<string, string> files = new(BaseFiles) { [FeatureClassRecord.PathFor("003-cart")] = FeatureClassRecord.Render(ChangeClass.Critical) };
        using RoutingRepository repository = await RoutingRepository.CreateAsync(files, cancellationToken);
        repository.Write("specs/003-cart/spec.md", "# Feature: Cart\n");
        repository.Write(FeatureClassRecord.PathFor("003-cart"), FeatureClassRecord.Render(ChangeClass.Fix));
        await repository.CommitAsync("lower the record", cancellationToken);

        RaiseReading reading = await ReadAsync(repository, RaiseInputs.None, cancellationToken);

        Assert.Equal([new ClassRaise("feature 003-cart on base", ChangeClass.Critical)], reading.Raises);
    }

    [Fact]
    public async Task LabelsAndTheRaiseInputRaiseAndUnknownValuesAreNoted()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using RoutingRepository repository = await RoutingRepository.CreateAsync(BaseFiles, cancellationToken);
        repository.Write("specs/005-tax/spec.md", "# Tax\n**Class**: huge\n");
        await repository.CommitAsync("spec", cancellationToken);

        RaiseReading reading = await ReadAsync(
            repository, new RaiseInputs(Branch: null, ["bug", "lockflight:class/feature", "lockflight:class/max"], Raise: "behaviour"), cancellationToken);

        Assert.Equal([new ClassRaise("label lockflight:class/feature", ChangeClass.Feature), new ClassRaise("workflow --raise", ChangeClass.Behaviour)], reading.Raises);
        Assert.Equal(2, reading.Notes.Count);
        Assert.Contains(reading.Notes, note => note.Contains("huge", StringComparison.Ordinal));
        Assert.Contains(reading.Notes, note => note.Contains("lockflight:class/max", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("**Class**: Feature\n", "feature")]
    [InlineData("**class**:critical.\n", "critical")]
    [InlineData("<!-- **Class**: critical -->\n", "none")]
    [InlineData("## Requirements\n**Class**: critical\n", "none")]
    public void TheClassLineIsReadFromTheHeaderOnly(string text, string expected) =>
        Assert.Equal(expected, ChangeClassNames.ToName(SpecClassLine.Read("# Spec\n" + text, out _)));

    private static async Task<RaiseReading> ReadAsync(RoutingRepository repository, RaiseInputs inputs, CancellationToken cancellationToken, string? headRef = "HEAD")
    {
        ChangeSetReading change = await ChangeSetReader.ReadAsync(repository.Root, RoutingRepository.BaseBranch, headRef, cancellationToken);
        Assert.True(change.Status == ChangeSetReadStatus.Read, change.Message);
        RaiseReading reading = await RaiseReader.ReadAsync(repository.Root, change.ChangeSet!, inputs, cancellationToken);
        Assert.Equal(RaiseReadStatus.Read, reading.Status);
        return reading;
    }
}
