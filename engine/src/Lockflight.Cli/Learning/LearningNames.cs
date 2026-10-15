namespace Lockflight.Cli.Learning;

/// <summary>Report spellings of the learning enums.</summary>
internal static class LearningNames
{
    public static string ToName(IncidentClass incidentClass) => incidentClass switch
    {
        IncidentClass.RuleIgnored => "rule known, agent reached for it anyway",
        IncidentClass.MissingKnowledge => "missing knowledge",
        IncidentClass.ToolingGap => "tooling gap",
        IncidentClass.ProcessSkipped => "process skipped",
        _ => throw new ArgumentOutOfRangeException(nameof(incidentClass), incidentClass, null),
    };

    public static string ToName(RemedyRung rung) => rung switch
    {
        RemedyRung.Analyzer => "analyzer",
        RemedyRung.Check => "check / test / policy",
        RemedyRung.Skill => "skill",
        RemedyRung.PathRule => "path rule",
        RemedyRung.AlwaysOnRule => "always-on rule",
        RemedyRung.Environment => "environment",
        _ => throw new ArgumentOutOfRangeException(nameof(rung), rung, null),
    };
}
