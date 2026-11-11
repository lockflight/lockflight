using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Hooks;

/// <summary>Assertions on what the hook decided, as its exit code and standard error show it.</summary>
internal static class HookAssert
{
    /// <summary>
    /// A block the guard decided: exit 2 without "internal error" in the output. The hook also exits 2 when it fails closed on an
    /// exception, so the exit code alone cannot tell a rule that fired from a crash.
    /// </summary>
    public static void Blocked(ExitCode exitCode, string error, string? because = null)
    {
        Assert.True(exitCode == ExitCode.HookBlock, because ?? $"exit {(int)exitCode}, not a block: {error}");
        Assert.DoesNotContain("internal error", error, StringComparison.Ordinal);
    }
}
