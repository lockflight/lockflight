namespace Lockflight.Cli.Gates.Suppressions;

/// <summary>How an added line silences a diagnostic or hides code from a check.</summary>
internal enum SuppressionKind
{
    /// <summary>A <c>#pragma warning disable</c> directive in C#.</summary>
    Pragma = 1,

    /// <summary>A <c>[SuppressMessage]</c> or <c>[UnconditionalSuppressMessage]</c> attribute, in code or in <c>GlobalSuppressions.cs</c>.</summary>
    SuppressMessage = 2,

    /// <summary>A diagnostic added to <c>&lt;NoWarn&gt;</c> (property or item attribute) in an MSBuild file.</summary>
    NoWarn = 3,

    /// <summary>A diagnostic added to <c>&lt;WarningsNotAsErrors&gt;</c> in an MSBuild file.</summary>
    WarningsNotAsErrors = 4,

    /// <summary>A diagnostic's severity in <c>.editorconfig</c> or a <c>.globalconfig</c> set below warning, or below what the line said before.</summary>
    LoweredSeverity = 5,

    /// <summary>A <c>#nullable disable</c> directive in C#.</summary>
    NullableDisable = 6,

    /// <summary>An <c>[ExcludeFromCodeCoverage]</c> attribute, which takes code out of the coverage the gates judge.</summary>
    ExcludeFromCodeCoverage = 7,
}
