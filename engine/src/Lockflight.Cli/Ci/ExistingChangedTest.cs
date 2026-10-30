namespace Lockflight.Cli.Ci;

/// <summary>An existing test the pull request changed, how its head version did on the base's code, and why.</summary>
internal sealed record ExistingChangedTest(string Key, ExistingChange State, string Why);
