using System.Text.RegularExpressions;

namespace Lockflight.Cli.Ci.Explain;

/// <summary>
/// A GitHub Actions run named by its id (the repository is the one <c>gh</c> finds from the working directory) or by its URL
/// (<c>https://github.com/&lt;owner&gt;/&lt;repo&gt;/actions/runs/&lt;id&gt;</c>, a job or attempt URL below it too).
/// </summary>
internal sealed partial record GitHubRun(string? Repository, string Id)
{
    /// <summary>The run <paramref name="text"/> names; null when it is neither a run id nor a run URL.</summary>
    public static GitHubRun? Parse(string text)
    {
        if (RunId().IsMatch(text))
        {
            return new GitHubRun(null, text);
        }

        Match url = RunUrl().Match(text);
        return url.Success ? new GitHubRun($"{url.Groups["owner"].Value}/{url.Groups["repo"].Value}", url.Groups["id"].Value) : null;
    }

    [GeneratedRegex("^[0-9]{1,20}$")]
    private static partial Regex RunId();

    [GeneratedRegex("^https://github\\.com/(?<owner>[A-Za-z0-9-]+)/(?<repo>[A-Za-z0-9._-]+)/actions/runs/(?<id>[0-9]{1,20})(?:[/?#].*)?$")]
    private static partial Regex RunUrl();
}
