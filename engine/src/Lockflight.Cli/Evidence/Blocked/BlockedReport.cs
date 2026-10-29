namespace Lockflight.Cli.Evidence.Blocked;

/// <summary>
/// "I stopped and a person must do something" (<c>lockflight.blocked/v1</c>), one shape for the CI judge, the engine's refusals
/// that need a person, the crew skills and a later autonomous mode. Advice for a person, generated from reason codes; never proof.
/// </summary>
/// <param name="Source"><c>ci</c>, <c>engine:&lt;command&gt;</c>, <c>crew:&lt;skill&gt;</c> or <c>autonomous</c>.</param>
/// <param name="CodeName">The reason code as written, shown as it is when <see cref="Code"/> is unknown.</param>
internal sealed record BlockedReport(DateTimeOffset At, string Source, BlockedReasonCode Code, string CodeName, string Summary, IReadOnlyList<BlockedNeed> Needs)
{
    public const string Schema = "lockflight.blocked/v1";

    /// <summary>The latest report the engine wrote in this clone, relative to the project root (git-ignored).</summary>
    public const string LocalRelativePath = ".lockflight/local/blocked.json";

    /// <summary>The feature the work belongs to; null when none.</summary>
    public string? Feature { get; init; }

    /// <summary>The commit the work stopped at; null when unknown.</summary>
    public string? Head { get; init; }

    public IReadOnlyList<BlockedQuestion> Questions { get; init; } = [];

    /// <summary>Files that show why (paths relative to the root).</summary>
    public IReadOnlyList<string> Evidence { get; init; } = [];

    /// <summary>Whether running again once the needs are met continues the same work.</summary>
    public bool Resumable { get; init; }

    public static BlockedReport Of(DateTimeOffset at, string source, BlockedReasonCode code, string summary, IReadOnlyList<BlockedNeed> needs) =>
        new(at, source, code, BlockedNames.ToName(code), summary, needs);
}
