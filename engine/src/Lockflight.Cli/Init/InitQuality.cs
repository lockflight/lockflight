namespace Lockflight.Cli.Init;

/// <summary>Which parts of the code-quality setup <c>init</c> was asked for; it offers them and never adds them by itself.</summary>
/// <param name="Analyzers"><c>--quality</c>: the recommended analyzers and the code metrics rules.</param>
/// <param name="ArchitectureTests"><c>--architecture-tests</c>: a layer dependency test project.</param>
internal sealed record InitQuality(bool Analyzers, bool ArchitectureTests);
