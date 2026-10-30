namespace Lockflight.Cli.Routing;

/// <summary>What a C# declaration declares, for the API signals.</summary>
internal enum DeclarationKind
{
    /// <summary>A namespace.</summary>
    Namespace = 1,

    /// <summary>A class, record, struct, interface, enum or delegate.</summary>
    Type = 2,

    /// <summary>A method, constructor, operator, property, indexer, event or field.</summary>
    Member = 3,
}
