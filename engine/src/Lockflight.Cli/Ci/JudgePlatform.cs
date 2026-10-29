namespace Lockflight.Cli.Ci;

/// <summary>Where <c>lockflight ci</c> runs; the template for each platform maps its variables to the command's flags.</summary>
internal enum JudgePlatform
{
    /// <summary>GitHub Actions.</summary>
    GitHub = 1,

    /// <summary>Bitbucket Pipelines.</summary>
    Bitbucket = 2,

    /// <summary>GitLab CI/CD.</summary>
    GitLab = 3,

    /// <summary>Azure Pipelines.</summary>
    Azure = 4,

    /// <summary>A developer's or agent's own checkout: the same checks, local inputs, evidence under <c>.lockflight/local/ci/</c>.</summary>
    Local = 5,
}
