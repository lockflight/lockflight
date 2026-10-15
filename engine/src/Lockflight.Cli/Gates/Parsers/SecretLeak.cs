namespace Lockflight.Cli.Gates.Parsers;

/// <summary>A gitleaks finding; the secret itself is never kept (gitleaks runs with <c>--redact</c>).</summary>
internal sealed record SecretLeak(string RuleId, string File, int Line, string Commit)
{
    public string Describe() => Commit.Length > 0 ? $"{RuleId} at {File}:{Line} ({Commit[..Math.Min(8, Commit.Length)]})" : $"{RuleId} at {File}:{Line}";
}
