namespace Lockflight.Cli.Ci;

/// <summary>The spellings of <see cref="JudgePlatform"/> on the command line and in evidence.</summary>
internal static class JudgePlatformNames
{
    public const string Accepted = "github, bitbucket, gitlab, azure or local";

    public static string ToName(JudgePlatform platform) => platform switch
    {
        JudgePlatform.GitHub => "github",
        JudgePlatform.Bitbucket => "bitbucket",
        JudgePlatform.GitLab => "gitlab",
        JudgePlatform.Azure => "azure",
        JudgePlatform.Local => "local",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unknown platform."),
    };

    /// <summary>The platform <paramref name="name"/> spells; null when it spells none.</summary>
    public static JudgePlatform? Parse(string name) => name switch
    {
        "github" => JudgePlatform.GitHub,
        "bitbucket" => JudgePlatform.Bitbucket,
        "gitlab" => JudgePlatform.GitLab,
        "azure" => JudgePlatform.Azure,
        "local" => JudgePlatform.Local,
        _ => null,
    };
}
