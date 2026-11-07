using System.Text.RegularExpressions;

namespace Lockflight.Cli.Init;

/// <summary>
/// The runner of the GitHub workflow's jobs that build the pull request's code, <c>judge</c> and <c>review</c>: Linux
/// (<c>ubuntu-latest</c>), or Windows (<c>windows-latest</c>, with bash as the jobs' shell) when a test project needs Windows
/// (<see cref="Gates.WindowsTargets.WindowsTargeting.WindowsTestProjects"/>). The report jobs read data only and stay on Linux. Only these two
/// labels are Lockflight's: a job whose <c>runs-on</c> is anything else (a self-hosted label, <c>windows-2025</c>, an
/// expression) runs where a person chose, and is never moved.
/// </summary>
internal static partial class JudgeRunner
{
    public const string LinuxLabel = "ubuntu-latest";
    public const string WindowsLabel = "windows-latest";

    private const string JobIndent = "    ";

    /// <summary>The shell the Windows jobs run their <c>run:</c> steps in; Windows runners default to PowerShell.</summary>
    private static readonly string[] BashDefaults = [JobIndent + "defaults:", JobIndent + "  run:", JobIndent + "    shell: bash"];

    /// <summary>The jobs that build: the runner moves for them, never for the report jobs.</summary>
    public static IReadOnlyList<string> BuildingJobs { get; } = ["judge", "review"];

    /// <summary>The <c>runs-on</c> value of a job (trimmed); null when the workflow has no such job, or it names no runner.</summary>
    public static string? RunsOn(string workflow, string job)
    {
        List<string> lines = [.. workflow.Split('\n')];
        return RunsOnLine(lines, job) is int index ? RunsOn().Match(lines[index].TrimEnd('\r')).Groups["label"].Value : null;
    }

    /// <summary>
    /// The workflow with its building jobs on <see cref="WindowsLabel"/> when <paramref name="windows"/>, else on
    /// <see cref="LinuxLabel"/>; a job on any other label is left as it is. Applying it twice changes nothing more.
    /// </summary>
    public static string Apply(string workflow, bool windows)
    {
        string newline = workflow.Contains("\r\n", StringComparison.Ordinal) ? "\r" : string.Empty;
        List<string> lines = [.. workflow.Split('\n')];
        foreach (string job in BuildingJobs)
        {
            if (RunsOnLine(lines, job) is not int index)
            {
                continue;
            }

            string label = RunsOn().Match(lines[index].TrimEnd('\r')).Groups["label"].Value;
            if (windows && label == LinuxLabel)
            {
                lines[index] = $"{JobIndent}runs-on: {WindowsLabel}{newline}";
                if (!HasDefaults(lines, job))
                {
                    lines.InsertRange(index + 1, BashDefaults.Select(line => line + newline));
                }
            }
            else if (!windows && label == WindowsLabel)
            {
                lines[index] = $"{JobIndent}runs-on: {LinuxLabel}{newline}";
                if (lines.Count > index + BashDefaults.Length
                    && BashDefaults.Select((line, offset) => lines[index + 1 + offset].TrimEnd('\r') == line).All(same => same))
                {
                    lines.RemoveRange(index + 1, BashDefaults.Length);
                }
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>Whether a label is a Windows runner by its name (<c>windows-latest</c>, <c>windows-2025</c>, <c>[self-hosted, windows]</c>).</summary>
    public static bool IsWindows(string label) => label.Contains("windows", StringComparison.OrdinalIgnoreCase);

    /// <summary>The index of the job's <c>runs-on</c> line: the first one at the job's indentation, inside the job.</summary>
    private static int? RunsOnLine(List<string> lines, string job)
    {
        if (JobRange(lines, job) is not (int start, int end))
        {
            return null;
        }

        for (int index = start + 1; index < end; index++)
        {
            if (RunsOn().IsMatch(lines[index].TrimEnd('\r')))
            {
                return index;
            }
        }

        return null;
    }

    private static bool HasDefaults(List<string> lines, string job) =>
        JobRange(lines, job) is (int start, int end) && lines.Skip(start + 1).Take(end - start - 1).Any(line => line.TrimEnd('\r') == BashDefaults[0]);

    /// <summary>The job's key line and the line after its last (the next key at the job's or a lower indentation), under <c>jobs:</c>.</summary>
    private static (int Start, int End)? JobRange(List<string> lines, string job)
    {
        int jobs = -1;
        for (int index = 0; index < lines.Count; index++)
        {
            if (lines[index].TrimEnd('\r') == "jobs:")
            {
                jobs = index;
                break;
            }
        }

        if (jobs < 0)
        {
            return null;
        }

        string key = $"  {job}:";
        for (int start = jobs + 1; start < lines.Count; start++)
        {
            if (lines[start].TrimEnd('\r').TrimEnd() != key)
            {
                continue;
            }

            int end = start + 1;
            while (end < lines.Count && !EndsJob(lines[end].TrimEnd('\r')))
            {
                end++;
            }

            return (start, end);
        }

        return null;
    }

    /// <summary>A line at the indentation of a job key or less, which is not blank or a comment, starts the next job or section.</summary>
    private static bool EndsJob(string line) =>
        line.Length > 0 && !line.TrimStart().StartsWith('#') && (line.Length - line.TrimStart().Length) <= 2;

    [GeneratedRegex(@"^    runs-on:\s*(?<label>.*?)\s*$")]
    private static partial Regex RunsOn();
}
