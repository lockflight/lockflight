using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class SpecQuotesTests
{
    private const string Spec = """
        # Supplier quotes

        - **FR-002**: A supplier that runs out of
          time MUST be skipped.
        - **FR-003**: Cancellation by the caller MUST stop the collection.
        """;

    [Fact]
    public void AQuoteFoundInTheSpecAfterWhitespaceNormalisationIsAnchored() =>
        Assert.Equal(SpecAnchor.Anchored, SpecQuotes.Check("A supplier that runs out of time MUST\nbe   skipped.", Spec));

    [Fact]
    public void AQuoteTheSpecDoesNotContainIsNotAnchored() =>
        Assert.Equal(SpecAnchor.NotAnchored, SpecQuotes.Check("A supplier that ignores its token MUST be timed out.", Spec));

    [Fact]
    public void TheCheckIsCaseSensitive() =>
        Assert.Equal(SpecAnchor.NotAnchored, SpecQuotes.Check("cancellation by the caller must stop the collection.", Spec));

    [Fact]
    public void AQuoteOfOneOrTwoWordsIsNotAnchored() =>
        Assert.Equal(SpecAnchor.NotAnchored, SpecQuotes.Check("MUST be", Spec));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AFindingWithoutAQuoteHasNone(string? quote) =>
        Assert.Equal(SpecAnchor.None, SpecQuotes.Check(quote, Spec));
}
