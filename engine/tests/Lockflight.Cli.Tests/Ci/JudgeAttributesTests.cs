using Lockflight.Cli.Ci;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Tests.Ci;

public sealed class JudgeAttributesTests
{
    [Fact]
    public async Task InACloneThePullRequestsAttributesNoLongerHideSourceLinesFromGit()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using GitHistoryFixture repository = await GitHistoryFixture.CreateAsync(cancellationToken);
        string before = await repository.Write("Price.cs", "class Price {}\n").Write("icon.png", "P\0NG").CommitAsync("base", cancellationToken);
        string after = await repository.Write(".gitattributes", "*.cs binary\n*.png -diff\n").Write("Price.cs", "class Price {}\nclass Fee {}\n").Write("icon.png", "P\0NG2")
            .CommitAsync("hide", cancellationToken);
        string? hidden = await RoutingGit.DiffAsync(repository.Root, [before, after], ["--numstat", "-z"], cancellationToken);

        Assert.Null(await JudgeAttributes.NeutraliseAsync(repository.Root, cancellationToken));
        Assert.Null(await JudgeAttributes.NeutraliseAsync(repository.Root, cancellationToken));
        string? shown = await RoutingGit.DiffAsync(repository.Root, [before, after], ["--numstat", "-z"], cancellationToken);

        Assert.Contains("Price.cs", NumstatDiff.BinaryPaths(hidden!));
        Assert.DoesNotContain("Price.cs", NumstatDiff.BinaryPaths(shown!));
        Assert.Contains("icon.png", NumstatDiff.BinaryPaths(shown!));
        Assert.Single(File.ReadAllLines(Path.Combine(repository.Root, ".git", "info", "attributes")), line => line == JudgeAttributes.Line);
    }
}
