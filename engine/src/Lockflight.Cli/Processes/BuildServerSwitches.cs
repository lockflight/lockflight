namespace Lockflight.Cli.Processes;

/// <summary>
/// The command-line switches that keep the .NET build servers (the compiler server VBCSCompiler, which runs a project's analyzers
/// and source generators, and the Razor build server) off for one <c>dotnet build</c> or <c>dotnet test</c>. Passed as global
/// properties they win over the project's own files: a <c>Directory.Build.props</c> that sets <c>UseSharedCompilation</c> back to
/// true is ignored, which the environment variables of <see cref="ProcessRunner.EngineVariables"/> alone cannot achieve.
/// </summary>
internal static class BuildServerSwitches
{
    /// <summary>The global properties, for every <c>dotnet build</c> and <c>dotnet test</c> the engine starts.</summary>
    public static IReadOnlyList<string> Properties { get; } = ["-p:UseSharedCompilation=false", "-p:UseRazorBuildServer=false"];

    /// <summary>
    /// For <c>dotnet build</c>: the properties and <c>--disable-build-servers</c> (SDK 7 and later), which also turns MSBuild node
    /// reuse and the MSBuild server off on the command line. <c>dotnet test</c> on Microsoft.Testing.Platform does not take that
    /// switch, so test runs get <see cref="Properties"/> alone.
    /// </summary>
    public static IReadOnlyList<string> Build { get; } = ["--disable-build-servers", .. Properties];
}
