namespace Lockflight.Cli.Hosting;

/// <summary>The verb of a command group (<c>spec</c> of <c>approve spec</c>) and the arguments that follow it.</summary>
internal sealed record CommandVerb(string Name, string[] Rest);
