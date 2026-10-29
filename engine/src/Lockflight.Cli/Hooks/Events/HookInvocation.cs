using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Hooks.Events;

/// <summary>What a handler works with once the payload is parsed and the project found.</summary>
/// <param name="ProjectDirectory">The session's project directory (<c>CLAUDE_PROJECT_DIR</c>, else the payload's cwd).</param>
internal sealed record HookInvocation(HookInput Input, Manifest Manifest, string ProjectDirectory, CliContext Context);
