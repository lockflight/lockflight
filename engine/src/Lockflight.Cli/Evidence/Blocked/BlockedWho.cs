namespace Lockflight.Cli.Evidence.Blocked;

/// <summary>Who must act on a need of a blocked report (<c>needs[].who</c>); <see cref="Unknown"/> keeps an unknown value as written.</summary>
internal enum BlockedWho
{
    /// <summary>A value this engine does not know.</summary>
    Unknown = 0,

    /// <summary>The person working with the agent (<c>person</c>).</summary>
    Person = 1,

    /// <summary>A code owner of the paths involved (<c>codeowner</c>).</summary>
    Codeowner = 2,

    /// <summary>A maintainer of the repository (<c>maintainer</c>).</summary>
    Maintainer = 3,
}
