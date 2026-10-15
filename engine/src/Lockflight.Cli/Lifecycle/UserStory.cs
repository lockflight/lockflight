namespace Lockflight.Cli.Lifecycle;

/// <summary>A <c>### User Story N - Title</c> section of spec.md and whether it has Given/When/Then acceptance scenarios.</summary>
internal sealed record UserStory(int Number, string Title, int Line, bool HasAcceptanceScenarios);
