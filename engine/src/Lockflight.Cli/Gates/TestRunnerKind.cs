namespace Lockflight.Cli.Gates;

/// <summary>Which test platform <c>dotnet test</c> drives in a directory: the SDK decides it from <c>global.json</c>.</summary>
internal enum TestRunnerKind
{
    /// <summary>VSTest, the default without a <c>test.runner</c> setting: xUnit v2 and most existing test projects.</summary>
    VSTest = 1,

    /// <summary>Microsoft.Testing.Platform, chosen by <c>"test": { "runner": "Microsoft.Testing.Platform" }</c> in <c>global.json</c>.</summary>
    MicrosoftTestingPlatform = 2,
}
