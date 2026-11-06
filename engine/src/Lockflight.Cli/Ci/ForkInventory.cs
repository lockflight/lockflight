using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Ci;

/// <summary>
/// What a red commit is compared with at its fork point: the keys of the tests listed there, and which of them were skipped or
/// marked explicit. New tests are the ones not in <see cref="Tests"/>; a known test may not be deleted, newly skipped or made explicit.
/// </summary>
internal sealed record ForkInventory(IReadOnlySet<string> Tests, IReadOnlySet<string> Skipped, IReadOnlySet<string> Explicit)
{
    public static ForkInventory From(IReadOnlyList<TestCase> inventory) => new(
        inventory.Select(test => test.Key).ToHashSet(StringComparer.Ordinal),
        inventory.Where(test => test.SkipReason is not null).Select(test => test.Key).ToHashSet(StringComparer.Ordinal),
        inventory.Where(test => test.IsExplicit).Select(test => test.Key).ToHashSet(StringComparer.Ordinal));
}
