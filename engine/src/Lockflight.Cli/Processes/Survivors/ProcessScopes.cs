namespace Lockflight.Cli.Processes.Survivors;

/// <summary>The platform's <see cref="IProcessScope"/>.</summary>
internal static class ProcessScopes
{
    /// <summary>Sets up this platform's mechanism for the rest of the process's life (it cannot be undone), or says why there is none.</summary>
    public static IProcessScope Begin() =>
        OperatingSystem.IsLinux() ? LinuxProcessScope.Begin()
        : OperatingSystem.IsWindows() ? WindowsProcessScope.Begin()
        : OperatingSystem.IsMacOS() ? MacProcessScope.Begin()
        : new NoProcessScope("processes left running are not looked for on this operating system yet");

    /// <summary>No mechanism: for a host that must not change the process it runs in (the engine's own tests).</summary>
    public static IProcessScope Off() => new NoProcessScope("processes left running are not looked for in this host");
}
