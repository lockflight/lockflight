using Lockflight.Cli.Gates.Ef;

namespace Lockflight.Cli.Init;

/// <summary>
/// The DbContexts with migrations that the <c>ef.pendingModelChanges</c> step checks under <c>auto</c> and no tool manifest pins
/// <c>dotnet-ef</c> for, and the manifests (relative to the root) that pin it for the others.
/// </summary>
internal sealed record EfToolState(IReadOnlyList<EfContext> Unpinned, IReadOnlyList<string> Pins)
{
    /// <summary>The step does not depend on the pin: no migrations, or a mode other than auto.</summary>
    public static EfToolState None { get; } = new([], []);

    /// <summary>Whether any context with migrations was found.</summary>
    public bool HasMigrations => Unpinned.Count > 0 || Pins.Count > 0;
}
