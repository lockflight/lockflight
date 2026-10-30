using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Workspace;

/// <summary>
/// The manifest a command judges with after the workspace shell's policy was applied (or was not needed), the shell's policy when
/// it was read, or why the command cannot go on and with which exit code.
/// </summary>
internal sealed record ShellPolicyApplication(Manifest? Manifest, ShellPolicy? Policy, string? Error, ExitCode Failure);
