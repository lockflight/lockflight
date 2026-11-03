namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>A test file that holds new tests of this red and existed at an earlier cycle's red (<see cref="SharedTestFiles"/>).</summary>
/// <param name="Path">Relative to the project root, '/'-separated.</param>
/// <param name="Cycles">The earlier cycles whose red had the file, in order.</param>
/// <param name="NewTests">The new tests in the file, as <c>Class.Method</c>.</param>
internal sealed record SharedTestFile(string Path, IReadOnlyList<int> Cycles, IReadOnlyList<string> NewTests);
