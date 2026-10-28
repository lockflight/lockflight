namespace Lockflight.Cli.Init;

/// <summary>The CI service whose pipeline file <c>init</c> writes for the <c>pr</c> gates.</summary>
internal enum CiPlatform
{
    /// <summary>GitHub Actions: <c>.github/workflows/lockflight-gates.yml</c> (the default).</summary>
    GitHub = 1,

    /// <summary>Azure Pipelines: a pipeline of its own, <c>.azure-pipelines/lockflight-gates.yml</c>.</summary>
    Azure = 2,

    /// <summary>Bitbucket Pipelines: <c>bitbucket-pipelines.yml</c>, the one pipeline file a Bitbucket repository has.</summary>
    Bitbucket = 3,

    /// <summary>GitLab CI/CD: a job in <c>.gitlab/ci/lockflight-gates.yml</c>, included from <c>.gitlab-ci.yml</c>.</summary>
    GitLab = 4,
}
