namespace Lockflight.Cli.Routing;

/// <summary>How strongly a change class requires a <see cref="RequirementKind"/>.</summary>
internal enum RequirementLevel
{
    /// <summary>Not required and not run.</summary>
    Off = 0,

    /// <summary>Required when it applies to the change (e.g. trace when there is a spec), else off.</summary>
    WhenApplicable = 1,

    /// <summary>Required: a FAIL or NOT-EARNED blocks.</summary>
    Required = 2,

    /// <summary>Required, and NOT-EARNED when no API key is configured to run it.</summary>
    RequiredWithKey = 3,

    /// <summary>Run and shown; never changes the verdict.</summary>
    Advisory = 4,
}
