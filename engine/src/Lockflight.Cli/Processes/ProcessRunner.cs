using System.ComponentModel;
using System.Diagnostics;

namespace Lockflight.Cli.Processes;

/// <summary>Runs tools as argv processes (no shell), with an environment that keeps builds hermetic and quiet.</summary>
internal static class ProcessRunner
{
    /// <summary>Exit code reported for a process killed at its time limit (128 + SIGKILL, as shells do).</summary>
    public const int TimedOutExitCode = 137;

    /// <summary>How long the runner waits for a process's output to end after the process has exited.</summary>
    public static readonly TimeSpan OutputDrainTime = TimeSpan.FromSeconds(2);

    private static readonly IReadOnlyDictionary<string, string?> NoVariables = new Dictionary<string, string?>(StringComparer.Ordinal);

    private static readonly string[] RunnerVariables =
    [
        "GITHUB_ENV", "GITHUB_OUTPUT", "GITHUB_PATH", "GITHUB_STATE", "GITHUB_STEP_SUMMARY",
        "ACTIONS_RUNTIME_TOKEN", "ACTIONS_ID_TOKEN_REQUEST_TOKEN", "ACTIONS_ID_TOKEN_REQUEST_URL",
    ];

    /// <summary>The variables every process the engine starts gets, on top of the engine's own environment.</summary>
    public static IReadOnlyDictionary<string, string?> EngineVariables { get; } = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        // MSBuild node reuse leaves server processes behind, which breaks hooks and sandboxes.
        ["MSBUILDDISABLENODEREUSE"] = "1",

        // The other build servers would outlive the build too: the compiler server (VBCSCompiler, which runs a project's analyzers
        // and source generators), the Razor build server and the MSBuild server. MSBuild reads the first two as properties, which a
        // project file can set back, so the engine's own builds and test runs also pass them as global properties
        // (BuildServerSwitches); a tool that builds by itself (dotnet ef) has only these. The CI judge also shuts the servers down
        // after each phase.
        ["UseSharedCompilation"] = "false",
        ["UseRazorBuildServer"] = "false",
        ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        ["DOTNET_NOLOGO"] = "1",

        // Read-only git calls (status, diff) must not take index.lock and collide with the user's or the agent's own commit.
        ["GIT_OPTIONAL_LOCKS"] = "0",

        // Snapshot libraries (Verify and others built on DiffEngine) open the developer's diff tool on a failed comparison; a test run
        // the engine starts (gates, red/green, reviews) must never pop windows on the person's screen.
        ["DiffEngine_Disabled"] = "true",
    };

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
    public static Task<ProcessResult> RunAsync(
        string fileName, IEnumerable<string> arguments, string workingDirectory, TimeSpan timeLimit, string? standardInputPath, CancellationToken cancellationToken) =>
        RunAsync(fileName, arguments, workingDirectory, timeLimit, standardInputPath, NoVariables, cancellationToken);

    /// <summary>
    /// Runs the process with <paramref name="variables"/> set in its environment (e.g. <c>GIT_INDEX_FILE</c> for a private index); a
    /// variable whose value is null is removed instead.
    /// </summary>
    public static Task<ProcessResult> RunAsync(
        string fileName, IEnumerable<string> arguments, string workingDirectory, TimeSpan timeLimit, IReadOnlyDictionary<string, string?> variables, CancellationToken cancellationToken) =>
        RunAsync(fileName, arguments, workingDirectory, timeLimit, standardInputPath: null, variables, cancellationToken);

    private static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        TimeSpan timeLimit,
        string? standardInputPath,
        IReadOnlyDictionary<string, string?> variables,
        CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = StartInfo(fileName, arguments, workingDirectory, redirectInput: standardInputPath is not null);
        // A caller's variables (a test project's run settings among them) cannot put back the CI runner's files and tokens.
        foreach ((string name, string? value) in variables.Where(variable => !RunnerVariables.Contains(variable.Key, StringComparer.OrdinalIgnoreCase)))
        {
            if (value is null)
            {
                startInfo.Environment.Remove(name);
            }
            else
            {
                startInfo.Environment[name] = value;
            }
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        using Process process = Start(startInfo);
        using OutputCapture standardOutput = new(process.StandardOutput.BaseStream);
        using OutputCapture standardError = new(process.StandardError.BaseStream);

        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeLimit);
        bool timedOut = false;
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
            timedOut = true;
        }

        bool outputHeld = await DrainAsync(startInfo, standardOutput, standardError);
        return new ProcessResult(
            timedOut ? TimedOutExitCode : process.ExitCode,
            standardOutput.Text(process.StandardOutput.CurrentEncoding),
            standardError.Text(process.StandardError.CurrentEncoding),
            stopwatch.ElapsedMilliseconds,
            timedOut)
        {
            OutputHeld = outputHeld,
        };
    }

    /// <summary>
    /// Runs a process that reads <paramref name="input"/> and writes binary output (<c>git cat-file --batch</c>, whose headers count
    /// bytes, not characters): standard output is returned as raw bytes. Past <paramref name="timeLimit"/> the process tree is killed.
    /// </summary>
    public static async Task<BinaryProcessResult> RunForBytesAsync(
        string fileName, IEnumerable<string> arguments, string workingDirectory, byte[] input, TimeSpan timeLimit, CancellationToken cancellationToken)
    {
        ProcessStartInfo startInfo = StartInfo(fileName, arguments, workingDirectory, redirectInput: true);
        using Process process = Start(startInfo);
        using OutputCapture standardOutput = new(process.StandardOutput.BaseStream);
        using OutputCapture standardError = new(process.StandardError.BaseStream);

        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeLimit);
        bool timedOut = false;
        try
        {
            try
            {
                await process.StandardInput.BaseStream.WriteAsync(input, limit.Token);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // The process closed its input early; its exit code says what happened.
            }

            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            timedOut = true;
        }

        bool outputHeld = await DrainAsync(startInfo, standardOutput, standardError);
        return new BinaryProcessResult(
            timedOut ? TimedOutExitCode : process.ExitCode, standardOutput.Bytes(), standardError.Text(process.StandardError.CurrentEncoding), timedOut)
        {
            OutputHeld = outputHeld,
        };
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

        foreach ((string name, string? value) in EngineVariables)
        {
            startInfo.Environment[name] = value;
        }

        // The CI runner's file commands and tokens: a build or test the engine starts (the pull request's code, in the CI judge's
        // job) must not set the next steps' environment or outputs. A guardrail: the files stay on disk under the runner's
        // temporary folder.
        foreach (string name in RunnerVariables)
        {
            startInfo.Environment.Remove(name);
        }

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

    /// <summary>
    /// Waits briefly for the output streams to end once the process has exited, then gives them up: a process it started (a build
    /// server, a daemon a test forgot) may have inherited them and hold them open until the job's own timeout. Returns whether they
    /// were given up.
    /// </summary>
    private static async Task<bool> DrainAsync(ProcessStartInfo startInfo, OutputCapture standardOutput, OutputCapture standardError)
    {
        Task streamsEnded = Task.WhenAll(standardOutput.Completion, standardError.Completion);
        if (await Task.WhenAny(streamsEnded, Task.Delay(OutputDrainTime, CancellationToken.None)) == streamsEnded)
        {
            return false;
        }

        standardOutput.Abandon();
        standardError.Abandon();
        HeldOutputs.Record(startInfo.ArgumentList.Count == 0
            ? Path.GetFileName(startInfo.FileName)
            : $"{Path.GetFileName(startInfo.FileName)} {startInfo.ArgumentList[0]}");
        return true;
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
