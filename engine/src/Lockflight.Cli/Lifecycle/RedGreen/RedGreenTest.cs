namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>A test accepted in a red/green cycle, with the requirements its traits name.</summary>
internal sealed record RedGreenTest(string Key, string MethodFilter, RedClass RedClass, IReadOnlyList<string> Requirements);
