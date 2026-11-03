using Lockflight.Cli.Config;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Tests.Routing;

public sealed class ChangeClassifierGoldenTests : ChangeClassifierCases
{
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EachFixtureIsClassifiedAsItsGoldenSays(string name)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (ChangeSet changeSet, Manifest? manifest) = await ReadAsync(name, extraFile: null, cancellationToken);

        Classification classification = ChangeClassifier.Classify(changeSet, Policy(manifest), []);

        VerifyFixtureGolden(name, Render(classification));
    }
}
