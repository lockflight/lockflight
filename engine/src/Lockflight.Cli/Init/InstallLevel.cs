namespace Lockflight.Cli.Init;

/// <summary>How much of Lockflight runs on a developer's machine; CI judges at every level. Not a manifest key.</summary>
internal enum InstallLevel
{
    /// <summary>CI and the Claude Code plugin (the default): the plugin's hooks, skills and commands, and the project settings.</summary>
    Plugin = 0,

    /// <summary>CI and native Claude Code settings (<c>init --native</c>): deny rules, sandbox, the CLAUDE.md block and the project's own hooks calling the engine, without the plugin.</summary>
    Native = 1,

    /// <summary>CI only (<c>init --ci-only</c>): the manifest, the engine pin and the CI file; nothing for Claude Code and no engine linked.</summary>
    CiOnly = 2,
}
