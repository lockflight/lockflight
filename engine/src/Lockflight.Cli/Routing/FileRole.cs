namespace Lockflight.Cli.Routing;

/// <summary>What a changed path is for routing (docs/guides/risk-routing.md): whether it counts toward size and which signals read it.</summary>
internal enum FileRole
{
    /// <summary>Matches the base branch's <c>routing.generatedPaths</c> or <c>routing.nonProduction</c>: left out of size and shape only.</summary>
    NonProduction = 1,

    /// <summary>On the engine's fixed generated list (lock files, EF Core snapshots and designer files with EF's shape).</summary>
    Generated = 2,

    /// <summary>A file of a test project.</summary>
    Test = 3,

    /// <summary>C#, F# or Visual Basic source of a production project, or a <c>.cs</c> file under no project.</summary>
    Production = 4,

    /// <summary>A Razor component or page (<c>.razor</c>, <c>.cshtml</c>) of a production project.</summary>
    Markup = 5,

    /// <summary>A test run or coverage setting (<c>*.runsettings</c>, <c>xunit.runner.json</c>, <c>testconfig.json</c>).</summary>
    TestConfig = 6,

    /// <summary>A non-source file inside a production project (<c>appsettings*.json</c>, resources).</summary>
    ProductionConfig = 7,

    /// <summary>A project or solution file.</summary>
    Project = 8,

    /// <summary>A file that changes every project's build (<c>Directory.Build.props</c>, <c>*.targets</c>, <c>global.json</c>, …).</summary>
    BuildWide = 9,

    /// <summary>Documentation, images, and Lockflight's own feature records.</summary>
    Docs = 10,

    /// <summary>Anything else: scripts, Dockerfiles, YAML, SQL outside projects.</summary>
    Other = 11,
}
