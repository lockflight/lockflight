namespace Lockflight.Cli.Processes.Survivors;

/// <summary>The platform's <see cref="IProcessScope"/>.</summary>
internal static class ProcessScopes
{
    /// <summary>
    /// Sets up this platform's mechanism for the rest of the process's life (it cannot be undone), or says why there is none.
    /// <paramref name="ci"/>: the run is a CI job's, not one a person or an agent started on their machine, so on macOS the engine
    /// may leave its caller's process group (<see cref="MacProcessScope"/>).
    /// </summary>
    public static IProcessScope Begin(bool ci) =>
        OperatingSystem.IsLinux() ? LinuxProcessScope.Begin()
        : OperatingSystem.IsWindows() ? WindowsProcessScope.Begin()
        : OperatingSystem.IsMacOS() ? MacProcessScope.Begin(ci)
        : new NoProcessScope("processes left running are not looked for on this operating system yet");

    /// <summary>No mechanism: for a host that must not change the process it runs in (the engine's own tests).</summary>
    public static IProcessScope Off(bool ci) => new NoProcessScope("processes left running are not looked for in this host");
}
