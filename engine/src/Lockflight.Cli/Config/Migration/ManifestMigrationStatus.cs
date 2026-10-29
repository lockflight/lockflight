namespace Lockflight.Cli.Config.Migration;

/// <summary>What the version 1 → 2 migration of <c>lockflight.yaml</c> did.</summary>
internal enum ManifestMigrationStatus
{
    /// <summary>The manifest is version 2 already; nothing to do.</summary>
    AlreadyCurrent = 1,

    /// <summary>The edited text is ready to write; it parses to the settings version 1 had, with the new sections spelled out.</summary>
    Migrated = 2,

    /// <summary>The file could not be edited safely (more than one YAML document, a flow-style section, a check set half-way);
    /// nothing is written and the person gets the snippet to add by hand.</summary>
    ManualEdit = 3,

    /// <summary>The version 1 manifest is invalid; the whole update stops before anything is written.</summary>
    Invalid = 4,
}
