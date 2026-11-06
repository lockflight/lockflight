using Lockflight.Cli.Hosting;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Doctor;

/// <summary>
/// The Git Bash that runs the plugin's hooks on Windows, found the way Claude Code and <c>scripts/dev-install.ps1</c> find it:
/// <c>CLAUDE_CODE_GIT_BASH_PATH</c>, else the <c>bin/bash.exe</c> of the Git for Windows that holds the <c>git</c> on PATH, else
/// Git for Windows' default folders, else <c>bash</c> on PATH. It is found as a full path because Windows, given a bare name,
/// looks in System32 before PATH, where WSL's <c>bash.exe</c> starts a Linux distribution instead (and fails without one).
/// </summary>
internal static class GitBash
{
    private const string GitFolder = "Git";
    private const string BinFolder = "bin";

    /// <summary>The Git Bash executable; null when none is found.</summary>
    public static string? Find(EnvironmentReader environment)
    {
        string bash = ExecutableName("bash");
        string? path = environment.Get(EnvironmentVariables.Path);
        List<string> candidates = [];
        if (environment.Get(EnvironmentVariables.GitBashPath) is { Length: > 0 } configured)
        {
            candidates.Add(configured);
        }

        if (Executables.FindOnPath("git", path) is string git && Path.GetDirectoryName(Path.GetDirectoryName(git)) is string gitRoot)
        {
            candidates.Add(Path.Combine(gitRoot, BinFolder, bash));
        }

        foreach (string variable in new[] { EnvironmentVariables.ProgramFiles, EnvironmentVariables.ProgramFilesX86 })
        {
            if (environment.Get(variable) is { Length: > 0 } programFiles)
            {
                candidates.Add(Path.Combine(programFiles, GitFolder, BinFolder, bash));
            }
        }

        return candidates.FirstOrDefault(File.Exists) ?? Executables.FindOnPath("bash", path);
    }

    private static string ExecutableName(string name) => OperatingSystem.IsWindows() ? $"{name}.exe" : name;
}
