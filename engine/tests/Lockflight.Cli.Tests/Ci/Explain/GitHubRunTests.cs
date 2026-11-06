using Lockflight.Cli.Ci.Explain;

namespace Lockflight.Cli.Tests.Ci.Explain;

public sealed class GitHubRunTests
{
    [Theory]
    [InlineData("1234567890", null, "1234567890")]
    [InlineData("https://github.com/acme/shop/actions/runs/42", "acme/shop", "42")]
    [InlineData("https://github.com/acme/shop.api/actions/runs/42/job/7", "acme/shop.api", "42")]
    [InlineData("https://github.com/acme/shop/actions/runs/42/attempts/2", "acme/shop", "42")]
    [InlineData("https://github.com/acme/shop/actions/runs/42?pr=3", "acme/shop", "42")]
    public void ARunIdOrRunUrlNamesTheRun(string text, string? repository, string id) =>
        Assert.Equal(new GitHubRun(repository, id), GitHubRun.Parse(text));

    [Theory]
    [InlineData("evidence.json")]
    [InlineData("https://github.com/acme/shop/pull/3")]
    [InlineData("https://example.com/acme/shop/actions/runs/42")]
    [InlineData("12ab")]
    [InlineData("")]
    public void AnythingElseIsNoRun(string text) => Assert.Null(GitHubRun.Parse(text));
}
