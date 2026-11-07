using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Lingering.Generator;

/// <summary>
/// A source generator that, when the fixture is armed, starts a daemon from inside the compiler and leaves it running after the
/// compiler exits (a test fixture). It generates nothing.
/// </summary>
[Generator]
public sealed class LingeringGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        string? markers = Environment.GetEnvironmentVariable("LOCKFLIGHT_FIXTURE_MARKERS");
        string? daemon = Environment.GetEnvironmentVariable("LOCKFLIGHT_FIXTURE_DAEMON");
        if (string.IsNullOrEmpty(markers) || string.IsNullOrEmpty(daemon))
        {
            return;
        }

        string token = Guid.NewGuid().ToString("N");
        bool windows = Path.DirectorySeparatorChar == '\\';
        string python = windows ? OnPath("python.exe") : "python3";
        ProcessStartInfo start = new(python, $"\"{daemon}\" generator {token}")
        {
            // On Windows the shell starts it, so it inherits none of the compiler's handles (which would hold the build's output open).
            UseShellExecute = windows,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = !windows,
            RedirectStandardOutput = !windows,
            RedirectStandardError = !windows,
        };
        Process.Start(start);
        string started = Path.Combine(markers, $"started-generator-{token}");
        for (int attempt = 0; attempt < 100 && !File.Exists(started); attempt++)
        {
            System.Threading.Thread.Sleep(100);
        }
    }

    private static string OnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator)
            .Select(directory => Path.Combine(directory, name))
            .FirstOrDefault(File.Exists) ?? name;
}
