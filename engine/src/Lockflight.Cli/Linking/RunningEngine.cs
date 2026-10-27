namespace Lockflight.Cli.Linking;

/// <summary>
/// The binary this process runs from, and whether it runs as a single file. A Native AOT engine does; a framework-dependent
/// build's launcher loads the engine assembly from beside it and fails once copied on its own into a project.
/// </summary>
/// <param name="Path">The running executable; null when the platform cannot tell.</param>
/// <param name="IsSingleFile">Whether the executable holds the whole engine (no engine assembly lies beside it).</param>
internal sealed record RunningEngine(string? Path, bool IsSingleFile)
{
    /// <summary>The engine this process is.</summary>
    public static RunningEngine Current { get; } = new(Environment.ProcessPath, !HasAssemblyBeside());

    // Not RuntimeFeature.IsDynamicCodeSupported: PublishAot writes that switch as false into every build's runtimeconfig, so it
    // is false in a framework-dependent build too.
    private static bool HasAssemblyBeside() =>
        File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, typeof(RunningEngine).Assembly.GetName().Name + ".dll"));
}
