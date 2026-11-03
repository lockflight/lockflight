using Lockflight.Cli.Config;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Tests.Routing;

public sealed class ChangeClassifierAddedFileFeatureAndFixTests : ChangeClassifierCases
{
    [Theory]
    [MemberData(nameof(CasesFromFeature))]
    public async Task AddingAProductionFileNeverLowersTheClass(string name)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        (ChangeSet changeSet, Manifest? manifest) = await ReadAsync(name, extraFile: null, cancellationToken);
        (ChangeSet larger, _) = await ReadAsync(name, extraFile: "tools/Extra.cs", cancellationToken);

        Classification before = ChangeClassifier.Classify(changeSet, Policy(manifest), []);
        Classification after = ChangeClassifier.Classify(larger, Policy(manifest), []);

        Assert.True(after.Class >= before.Class, $"{after.Class} after adding a file, {before.Class} before");
        Assert.True(after.Class >= ChangeClass.Behaviour);
    }
}
