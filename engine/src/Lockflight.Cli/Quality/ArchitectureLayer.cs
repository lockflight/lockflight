namespace Lockflight.Cli.Quality;

/// <summary>The layers the generated architecture test knows, inner first: a layer may depend only on the layers before it.</summary>
internal enum ArchitectureLayer
{
    /// <summary>The domain model: depends on no other layer.</summary>
    Domain = 1,

    /// <summary>Use cases: may depend on the domain.</summary>
    Application = 2,

    /// <summary>Persistence and other adapters: may depend on the application and the domain.</summary>
    Infrastructure = 3,

    /// <summary>The entry point (web API, host): may depend on every other layer.</summary>
    Api = 4,
}
