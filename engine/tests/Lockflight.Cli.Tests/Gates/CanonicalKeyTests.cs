using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Tests.Gates;

public sealed class CanonicalKeyTests
{
    [Fact]
    public void AValueThatContainsSeparatorsCannotReadAsTwoFields() =>
        Assert.NotEqual(new CanonicalKey().Add("a", "1;1:b=1:2").ToString(), new CanonicalKey().Add("a", "1").Add("b", "2").ToString());

    [Fact]
    public void AnAbsentValueDiffersFromEveryPresentOne()
    {
        Assert.NotEqual(new CanonicalKey().Add("a", null).ToString(), new CanonicalKey().Add("a", "-").ToString());
        Assert.NotEqual(new CanonicalKey().Add("a", null).ToString(), new CanonicalKey().Add("a", string.Empty).ToString());
    }
}
