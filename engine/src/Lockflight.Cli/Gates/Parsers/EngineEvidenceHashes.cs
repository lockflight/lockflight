using System.Text.RegularExpressions;

namespace Lockflight.Cli.Gates.Parsers;

/// <summary>
/// Recognizes a gitleaks finding that is one of the engine's own recorded hashes. The engine writes SHA-256 digests and commit ids
/// under file paths and names (<c>"tests/Shop.Tests/TokenTests.cs": "&lt;sha-256&gt;"</c>), which the generic API key rule reads
/// as a key and its value. A finding is the engine's hash only when all of these hold: the rule is the generic one, the file is a
/// red/green, approval or review record under <c>.lockflight/features/&lt;id&gt;/</c>, the whole match is one JSON name (path
/// characters only) and its value, and that value — the secret gitleaks found — is exactly 40 or 64 lowercase hex digits. Anything
/// else in those files (a token of a known shape, a value that is not a bare digest) stays a finding, and so does the same line in
/// any other file.
/// </summary>
internal static partial class EngineEvidenceHashes
{
    private const string GenericRule = "generic-api-key";

    public static bool Recognize(string ruleId, string file, string match, string secret) =>
        ruleId == GenericRule
        && EvidenceFile().IsMatch(file.Replace('\\', '/'))
        && NameAndHash().Match(match) is { Success: true } pair
        && pair.Groups["hash"].Value == secret;

    [GeneratedRegex(@"(?:^|/)\.lockflight/features/[^/]+/(?:redgreen|approvals/[^/]+|reviews/[^/]+)\.json\z")]
    private static partial Regex EvidenceFile();

    [GeneratedRegex(@"^[\w./ -]*"":[ \t]*""(?<hash>[0-9a-f]{40}|[0-9a-f]{64})""\z")]
    private static partial Regex NameAndHash();
}
