using Lockflight.Cli.Gates.Baselines;

namespace Lockflight.Cli.Tests.Gates.Baselines;

public sealed class KnownFailingTestsTests : IDisposable
{
    private readonly DirectoryInfo projectRoot = Directory.CreateTempSubdirectory("lockflight-kft-");

    [Fact]
    public void AcceptedFailuresRoundTripWithoutDuplicates()
    {
        KnownFailingTests baseline = new(projectRoot.FullName, unitPath: null);

        baseline.Save(["B.Test(x: \"1\")", "A.Test", "A.Test"]);

        Assert.Equal(["A.Test", "B.Test(x: \"1\")"], baseline.Load().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void WithoutABaselineNoFailureIsKnown() =>
        Assert.Empty(new KnownFailingTests(projectRoot.FullName, unitPath: null).Load());

    [Fact]
    public void EachUnitOfAWorkspaceHasItsOwnBaseline()
    {
        KnownFailingTests orders = new(projectRoot.FullName, "services/orders");
        KnownFailingTests billing = new(projectRoot.FullName, "services/billing");

        orders.Save(["Orders.Tests.Legacy"]);
        billing.Save(["Billing.Tests.Legacy"]);

        Assert.Equal(".lockflight/baselines/services/orders/known-failing-tests.json", orders.RelativePath);
        Assert.Equal(["Orders.Tests.Legacy"], orders.Load());
        Assert.Equal(["Billing.Tests.Legacy"], billing.Load());
        Assert.Empty(new KnownFailingTests(projectRoot.FullName, unitPath: null).Load());
    }

    public void Dispose() => projectRoot.Delete(recursive: true);
}
