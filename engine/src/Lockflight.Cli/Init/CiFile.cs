namespace Lockflight.Cli.Init;

/// <summary>A CI file <c>init</c> writes when it does not exist yet, relative to the project root.</summary>
internal sealed record CiFile(string RelativePath, string Content);
