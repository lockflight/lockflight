namespace Lockflight.Cli.Quality;

/// <summary>A project that holds one layer: its path relative to the root ('/'-separated) and the assembly it builds.</summary>
internal sealed record LayerProject(ArchitectureLayer Layer, string RelativePath, string AssemblyName);
