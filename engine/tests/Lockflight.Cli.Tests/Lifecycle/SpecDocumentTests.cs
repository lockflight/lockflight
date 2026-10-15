using Lockflight.Cli.Lifecycle;

namespace Lockflight.Cli.Tests.Lifecycle;

public sealed class SpecDocumentTests
{
    private const string Spec = """
        # Feature Specification: Shipping

        <!--
          - **FR-999**: template guidance is not a requirement [NEEDS CLARIFICATION: ignored]
        -->

        ## User Scenarios & Testing *(mandatory)*

        ### User Story 1 - Pay for shipping (Priority: P1)

        1. **Given** a 2.1 kg parcel, **When** the order is priced, **Then** shipping is 10.00

        ### User Story 2 - Free shipping

        No scenarios yet.

        ### Edge Cases

        ## Requirements *(mandatory)*

        - **FR-001**: System MUST charge 5.00 for the first kilogram
        - **FR-002**: System MUST charge 2.50 per further started kilogram
        - **FR-002**: Duplicate on purpose
        - **FR-003**: Orders of [NEEDS CLARIFICATION: threshold?] or more ship free
        """;

    [Fact]
    public void RequirementsStoriesAndClarificationsAreReadOutsideComments()
    {
        SpecDocument spec = SpecDocument.Parse(Spec);

        Assert.Equal(["FR-001", "FR-002", "FR-002", "FR-003"], spec.Requirements.Select(requirement => requirement.Id));
        Assert.Equal(["FR-002"], spec.DuplicateRequirementIds);
        Assert.Single(spec.OpenClarificationLines);
        Assert.Equal(2, spec.Stories.Count);
        Assert.True(spec.Stories[0].HasAcceptanceScenarios);
        Assert.Equal("Free shipping", spec.Stories[1].Title);
        Assert.False(spec.Stories[1].HasAcceptanceScenarios);
        Assert.Empty(spec.Placeholders);
    }

    [Fact]
    public void AnUntouchedSpecKitTemplateIsFullOfPlaceholders()
    {
        const string template = """
            # Feature Specification: [FEATURE NAME]

            **Feature Branch**: `[###-feature-name]`

            **Created**: [DATE]

            **Input**: User description: "$ARGUMENTS"

            ### User Story 1 - [Brief Title] (Priority: P1)

            1. **Given** [initial state], **When** [action], **Then** [expected outcome]

            - **FR-001**: System MUST [specific capability, e.g., "allow users to create accounts"]
            - **FR-003**: Users MUST be able to [key interaction, e.g., "reset their password"]
            """;

        SpecDocument spec = SpecDocument.Parse(template);

        Assert.Equal([1, 3, 5, 7, 9, 11, 13, 14], spec.Placeholders.Select(placeholder => placeholder.Line));
        Assert.Equal("[FEATURE NAME]", spec.Placeholders[0].Text);
    }

    [Fact]
    public void LinksClarificationsAndShortTagsAreNotPlaceholders()
    {
        SpecDocument spec = SpecDocument.Parse("""
            See [the ADR](docs/adr.md) and [RFC 7231][rfc].
            - **FR-001**: Orders of [NEEDS CLARIFICATION: threshold?] ship free [P1]
            """);

        Assert.Empty(spec.Placeholders);
    }
}
