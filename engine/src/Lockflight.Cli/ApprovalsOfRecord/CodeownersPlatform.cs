namespace Lockflight.Cli.ApprovalsOfRecord;

/// <summary>
/// How a platform reads CODEOWNERS: where it looks, in its order (the first file present is the one it uses), and whether it has
/// sections (GitLab). A platform without CODEOWNERS (Azure Repos: required reviewers are a branch policy, which git cannot show)
/// has no locations.
/// </summary>
internal sealed record CodeownersPlatform(IReadOnlyList<string> Locations, bool Sections)
{
    public static CodeownersPlatform GitHub { get; } = new([".github/CODEOWNERS", "CODEOWNERS", "docs/CODEOWNERS"], Sections: false);

    public static CodeownersPlatform GitLab { get; } = new(["CODEOWNERS", "docs/CODEOWNERS", ".gitlab/CODEOWNERS"], Sections: true);

    /// <summary>Bitbucket's code owners (a CODEOWNERS file; the lookup order is not yet verified on a live workspace).</summary>
    public static CodeownersPlatform Bitbucket { get; } = new([".bitbucket/CODEOWNERS", "CODEOWNERS", "docs/CODEOWNERS"], Sections: false);

    public static CodeownersPlatform None { get; } = new([], Sections: false);

    /// <summary>A local run, which does not know the platform: the first file present at any platform's location, without sections.</summary>
    public static CodeownersPlatform Local { get; } = new([".github/CODEOWNERS", ".gitlab/CODEOWNERS", ".bitbucket/CODEOWNERS", "CODEOWNERS", "docs/CODEOWNERS"], Sections: false);
}
