using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Updating;

/// <summary>
/// Stands in for the processes a package update starts: <c>dotnet tool install</c> lays out a tool path the way the SDK does
/// (<c>lockflight</c> linked into <c>.store/lockflight.cli/&lt;version&gt;/…</c>) for <see cref="Newest"/>, and every other
/// process (the installed engine) succeeds. Records each call.
/// </summary>
internal sealed class FakeToolFeed(string newest)
{
    public string Newest { get; set; } = newest;

    public bool Fails { get; set; }

    public List<(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory)> Calls { get; } = [];

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        Calls.Add((fileName, arguments, workingDirectory));
        if (fileName != "dotnet")
        {
            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty, 0, false));
        }

        if (Fails)
        {
            return Task.FromResult(new ProcessResult(1, string.Empty, "error NU1101: Unable to find package Lockflight.Cli.", 0, false));
        }

        string toolPath = arguments[arguments.ToList().IndexOf("--tool-path") + 1];
        LayOut(toolPath, Newest);
        return Task.FromResult(new ProcessResult(0, "Tool 'lockflight.cli' was successfully installed.", string.Empty, 0, false));
    }

    /// <summary>A tool path holding <paramref name="version"/> the way <c>dotnet tool install --tool-path</c> leaves it.</summary>
    public static void LayOut(string toolPath, string version)
    {
        string relative = Path.Combine(".store", "lockflight.cli", version, "lockflight.cli.osx-arm64", version, "tools", "any", "osx-arm64", "lockflight");
        string binary = Path.Combine(toolPath, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
        File.WriteAllText(binary, "#!/bin/sh\n");
        File.CreateSymbolicLink(Path.Combine(toolPath, "lockflight"), relative);
    }
}
