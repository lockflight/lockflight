namespace Lockflight.Cli.Init;

/// <summary>Command-line spellings of <see cref="CiPlatform"/>.</summary>
internal static class CiPlatformNames
{
    public const string Choices = "github, azure, bitbucket or gitlab";

    public static bool TryParse(string name, out CiPlatform platform)
    {
        CiPlatform? parsed = name switch
        {
            "github" => CiPlatform.GitHub,
            "azure" => CiPlatform.Azure,
            "bitbucket" => CiPlatform.Bitbucket,
            "gitlab" => CiPlatform.GitLab,
            _ => null,
        };
        platform = parsed ?? CiPlatform.GitHub;
        return parsed is not null;
    }
}
