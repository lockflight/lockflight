namespace Lockflight.Cli.Review;

/// <summary>What a brace-delimited scope of a C# file holds, for deciding what <see cref="CSharpSignatures"/> lists inside it.</summary>
internal enum SignatureScope
{
    /// <summary>A namespace: its types are listed.</summary>
    Namespace = 1,

    /// <summary>A class, struct or record: its members that are not private are listed.</summary>
    Type = 2,

    /// <summary>An interface: all its members are listed.</summary>
    Interface = 3,

    /// <summary>An enum: listed with its members on one line.</summary>
    Enum = 4,

    /// <summary>A member body: not listed, only the member's signature.</summary>
    Code = 5,
}
