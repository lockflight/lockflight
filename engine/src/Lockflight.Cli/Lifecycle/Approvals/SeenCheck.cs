namespace Lockflight.Cli.Lifecycle.Approvals;

/// <summary>
/// The comparison of a feature's artifacts with the record of what the person could last see: when that record was taken and the
/// artifacts whose content differs from it (empty when none does).
/// </summary>
internal sealed record SeenCheck(SeenWhen When, IReadOnlyList<string> Changed);
