namespace Lockflight.Cli.ApprovalsOfRecord;

/// <summary>
/// One rule of a CODEOWNERS file: its pattern, the owners it names (empty for a rule that leaves paths without an owner), the
/// section it belongs to (GitLab; empty on GitHub), whether that section is optional (GitLab's <c>^[Section]</c>: its owners are
/// never required) and its line, for messages.
/// </summary>
internal sealed record CodeownersRule(string Pattern, IReadOnlyList<string> Owners, string Section, bool Optional, int Line);
