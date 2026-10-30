namespace Lockflight.Cli.Routing;

/// <summary>What a changed line of a project or props file does, for the project signals.</summary>
internal enum ProjectLineKind
{
    /// <summary>A package reference or version, or a line whose only change is a version value.</summary>
    Package = 1,

    /// <summary>A <c>&lt;ProjectReference&gt;</c>.</summary>
    Reference = 2,

    /// <summary>A property or item that changes what the build enforces or compiles (analyzers, warnings as errors, imports, outside sources).</summary>
    PolicyProperty = 3,

    /// <summary>Any other line.</summary>
    Other = 4,
}
