namespace Lockflight.Cli.Lifecycle;

/// <summary>A task in tasks.md: <c>- [ ] T012 [P] [US1] (FR-001, FR-003) Description</c>.</summary>
internal sealed record TaskLine(string Id, int? Story, IReadOnlyList<string> Requirements, bool IsTest, bool IsDone, int Line, string Description);
