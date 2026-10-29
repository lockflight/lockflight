namespace Lockflight.Cli.Config;

/// <summary>
/// From which class each requirement switches on (<c>routing.require</c>); it stays on above it. Null means <c>none</c>: the
/// requirement never switches on.
/// </summary>
/// <param name="SpecFrom">A spec is required from this class.</param>
/// <param name="ReviewFrom">The review runs (with an API key) from this class.</param>
/// <param name="ReviewKeyFrom">Without an API key the verdict is NOT-EARNED from this class; null never.</param>
/// <param name="ArchitectureFrom">Declared architecture tests are required from this class.</param>
/// <param name="ArchitectureDeclaredFrom">From this class a project without declared architecture tests is NOT-EARNED; null never.</param>
internal sealed record RoutingRequirements(
    ChangeClass SpecFrom, ChangeClass ReviewFrom, ChangeClass? ReviewKeyFrom, ChangeClass ArchitectureFrom, ChangeClass? ArchitectureDeclaredFrom);
