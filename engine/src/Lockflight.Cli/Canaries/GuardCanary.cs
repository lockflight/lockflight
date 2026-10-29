using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hooks.Guard;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Canaries;

/// <summary>
/// The self-test's guard canary: an Edit of <c>lockflight.yaml</c>, sent through <c>lockflight hook pre-tool-use</c> as Claude Code
/// sends it, must be blocked. Its harmless twin, a Write in <c>src/</c>, must be allowed: a guard that blocks everything would
/// otherwise look as if it caught the canary. The engine's canary suite plants one in front of every guard rule.
/// </summary>
internal static class GuardCanary
{
    public const string Name = "guard: an Edit of lockflight.yaml is blocked";

    private static readonly string[] PreToolUse = ["hook", "pre-tool-use"];

    public static async Task<CanaryReport> RunAsync(CanaryScratch scratch, CancellationToken cancellationToken)
    {
        CanaryRun twin = await scratch.RunAsAgentAsync(PreToolUse, FileCall(scratch, "Write", scratch.PathOf("src/ok.txt")), cancellationToken);
        if (twin.ExitCode != ExitCode.Pass)
        {
            return new CanaryReport(Name, CanaryResult.Broken, $"the guard blocks a Write in src/ too (exit {(int)twin.ExitCode}, {twin.FirstLine}), so its blocks prove nothing")
            {
                Control = ProtectedPathRule.RuleId,
            };
        }

        CanaryRun planted = await scratch.RunAsAgentAsync(PreToolUse, FileCall(scratch, "Edit", scratch.PathOf(Manifest.FileName)), cancellationToken);
        CanaryReport report = planted.ExitCode == ExitCode.HookBlock
            ? new CanaryReport(Name, CanaryResult.Caught, planted.FirstLine)
            : new CanaryReport(Name, CanaryResult.Missed, $"the hook exited {(int)planted.ExitCode} instead of blocking (2)");
        return report with { Control = ProtectedPathRule.RuleId };
    }

    private static string FileCall(CanaryScratch scratch, string tool, string path) =>
        scratch.ToolCall(tool, new JsonObject { ["file_path"] = path, ["old_string"] = "version: 1", ["new_string"] = "version: 1\n# x", ["content"] = "ok\n" });
}
