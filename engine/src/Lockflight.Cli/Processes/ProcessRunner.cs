using System.ComponentModel;
using System.Diagnostics;

namespace Lockflight.Cli.Processes;

/// <summary>Runs tools as argv processes (no shell), with an environment that keeps builds hermetic and quiet.</summary>
internal static class ProcessRunner
{
    /// <summary>Exit code reported for a process killed at its time limit (128 + SIGKILL, as shells do).</summary>
    public const int TimedOutExitCode = 137;

    public static Task<ProcessResult> RunAsync(string fileName, IEnumerable<string> arguments, string workingDirectory, CancellationToken cancellationToken) =>
        RunAsync(fileName, arguments, workingDirectory, Timeout.InfiniteTimeSpan, cancellationToken);

    /// <summary>Runs the process; past <paramref name="timeLimit"/> the whole process tree is killed (build servers and test hosts included).</summary>
    public static Task<ProcessResult> RunAsync(
        string fileName, IEnumerable<string> arguments, string workingDirectory, TimeSpan timeLimit, CancellationToken cancellationToken) =>
        RunAsync(fileName, arguments, workingDirectory, timeLimit, standardInputPath: null, cancellationToken);

    /// <summary>
    /// Runs a process with the given file as its standard input (e.g. <c>ssh-keygen -Y verify</c> reads the message there). Output and
    /// error are drained while the input is written, so a process that writes before reading all its input cannot deadlock; writing
    /// the input counts against <paramref name="timeLimit"/>.
    /// </summary>
    /// <exception cref="ExecutableNotFoundException"><paramref name="fileName"/> is not installed or not on PATH.</exception>
    public static async Task<ProcessResult> RunAsync(
        string fileName, IEnumerable<string> arguments, string workingDirectory, TimeSpan timeLimit, string? standardInputPath, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = StartInfo(fileName, arguments, workingDirectory, redirectInput: standardInputPath is not null);
        Stopwatch stopwatch = Stopwatch.StartNew();
        using Process process = Start(startInfo);
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> standardError = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeLimit);
        try
        {
            if (standardInputPath is not null)
            {
                await WriteInputAsync(process, standardInputPath, limit.Token);
            }

            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            return new ProcessResult(TimedOutExitCode, await standardOutput, await standardError, stopwatch.ElapsedMilliseconds, TimedOut: true);
        }

        return new ProcessResult(process.ExitCode, await standardOutput, await standardError, stopwatch.ElapsedMilliseconds, TimedOut: false);
    }

    private static ProcessStartInfo StartInfo(string fileName, IEnumerable<string> arguments, string workingDirectory, bool redirectInput)
    {
        ProcessStartInfo startInfo = new(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = redirectInput,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // MSBuild node reuse leaves server processes behind, which breaks hooks and sandboxes.
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        // Read-only git calls (status, diff) must not take index.lock and collide with the user's or the agent's own commit.
        startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        return startInfo;
    }

    private static Process Start(ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start '{startInfo.FileName}'.");
        }
        catch (Win32Exception exception) when (Directory.Exists(startInfo.WorkingDirectory))
        {
            // A missing working directory fails the same way; only a missing executable gets this message.
            throw new ExecutableNotFoundException(startInfo.FileName, exception);
        }
    }

    private static async Task WriteInputAsync(Process process, string standardInputPath, CancellationToken cancellationToken)
    {
        try
        {
            // Raw bytes: a signature is over the file's exact bytes, which a text round-trip through the console encoding could change.
            await using (FileStream input = File.OpenRead(standardInputPath))
            {
                await input.CopyToAsync(process.StandardInput.BaseStream, cancellationToken);
            }

            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The process closed its input early (it exited, or it needs no more); its exit code and output say what happened.
        }
    }
}
