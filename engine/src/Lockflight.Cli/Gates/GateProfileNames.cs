namespace Lockflight.Cli.Gates;

/// <summary>Command-line spellings of <see cref="GateProfile"/>.</summary>
internal static class GateProfileNames
{
    public static bool TryParse(string name, out GateProfile profile)
    {
        GateProfile? parsed = name switch
        {
            "edit" => GateProfile.Edit,
            "stop" => GateProfile.Stop,
            "pr" => GateProfile.PullRequest,
            _ => null,
        };
        profile = parsed ?? default;
        return parsed is not null;
    }

    public static string ToName(GateProfile profile) => profile switch
    {
        GateProfile.Edit => "edit",
        GateProfile.Stop => "stop",
        GateProfile.PullRequest => "pr",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown gate profile."),
    };
}
