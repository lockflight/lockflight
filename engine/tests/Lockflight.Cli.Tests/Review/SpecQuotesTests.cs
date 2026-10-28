using Lockflight.Cli.Review;

namespace Lockflight.Cli.Tests.Review;

public sealed class SpecQuotesTests
{
    private const string Spec = """
        # Supplier quotes

        - **FR-002**: A supplier that runs out of
          time MUST be skipped.
        - **FR-003**: Cancellation by the caller MUST stop the collection.
        - **FR-004**: The `retry_count` setting MUST cap retries at 2 * 3 attempts.

        1. **Given** a supplier that is slow, **When** the caller cancels, **Then** the collection _stops_.
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

    [Fact]
    public void AnAcceptanceScenarioQuotedWithoutEmphasisIsAnchored() =>
        Assert.Equal(
            SpecAnchor.Anchored,
            SpecQuotes.Check("Given a supplier that is slow, When the caller cancels, Then the collection stops.", Spec));

    [Fact]
    public void AnAcceptanceScenarioQuotedWithItsEmphasisIsAnchored() =>
        Assert.Equal(
            SpecAnchor.Anchored,
            SpecQuotes.Check("**Given** a supplier that is slow, **When** the caller cancels, **Then** the collection _stops_.", Spec));

    [Fact]
    public void AnAcceptanceScenarioQuotedWithOneWordChangedIsNotAnchored() =>
        Assert.Equal(
            SpecAnchor.NotAnchored,
            SpecQuotes.Check("Given a supplier that is fast, When the caller cancels, Then the collection stops.", Spec));

    [Fact]
    public void ARequirementQuotedWithoutItsCodeMarkersIsAnchored() =>
        Assert.Equal(SpecAnchor.Anchored, SpecQuotes.Check("The retry_count setting MUST cap retries", Spec));

    [Theory]
    [InlineData("The retrycount setting MUST cap retries")]
    [InlineData("The retry count setting MUST cap retries")]
    [InlineData("The `retry_count_` setting MUST cap retries")]
    public void AnIdentifierWithUnderscoresMustMatchExactly(string quote) =>
        Assert.Equal(SpecAnchor.NotAnchored, SpecQuotes.Check(quote, Spec));

    [Fact]
    public void AnAsteriskBetweenSpacesIsNotTakenForEmphasis() =>
        Assert.Equal(SpecAnchor.NotAnchored, SpecQuotes.Check("cap retries at 2 3 attempts.", Spec));

    [Fact]
    public void AnAsteriskBetweenSpacesStillMatchesItself() =>
        Assert.Equal(SpecAnchor.Anchored, SpecQuotes.Check("cap retries at 2 * 3 attempts.", Spec));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AFindingWithoutAQuoteHasNone(string? quote) =>
        Assert.Equal(SpecAnchor.None, SpecQuotes.Check(quote, Spec));
}
