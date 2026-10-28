namespace Lockflight.Cli.Rules;

/// <summary>The rules audit's result; <see cref="Scanned"/> is false outside a git repository.</summary>
internal sealed record RulesAuditReport(bool Scanned, IReadOnlyList<AuditedRule> Rules)
{
    public static RulesAuditReport NotARepository { get; } = new(Scanned: false, []);
}
