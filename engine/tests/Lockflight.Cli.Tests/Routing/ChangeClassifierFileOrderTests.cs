using Lockflight.Cli.Config;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Tests.Routing;

public sealed class ChangeClassifierFileOrderTests : ChangeClassifierCases
{
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TheClassificationDoesNotDependOnFileOrder(string name)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (ChangeSet changeSet, Manifest? manifest) = await ReadAsync(name, extraFile: null, cancellationToken);
        ChangeSet reversed = changeSet with { Files = [.. changeSet.Files.Reverse()] };
        ChangeSet shuffled = changeSet with { Files = [.. changeSet.Files.OrderBy(file => file.Path.Length).ThenByDescending(file => file.Path, StringComparer.Ordinal)] };

        string expected = Render(ChangeClassifier.Classify(changeSet, Policy(manifest), []));

        Assert.Equal(expected, Render(ChangeClassifier.Classify(reversed, Policy(manifest), [])));
        Assert.Equal(expected, Render(ChangeClassifier.Classify(shuffled, Policy(manifest), [])));
    }
}
