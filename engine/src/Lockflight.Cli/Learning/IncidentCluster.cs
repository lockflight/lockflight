namespace Lockflight.Cli.Learning;

/// <summary>Ledger incidents sharing a fingerprint, with the suggested classification and remedy.</summary>
internal sealed record IncidentCluster(
    string Fingerprint,
    int Count,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    string Example,
    IncidentClass Class,
    RemedyRung Rung,
    string Suggestion);
