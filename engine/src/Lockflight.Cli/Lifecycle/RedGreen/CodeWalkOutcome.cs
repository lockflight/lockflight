namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>How a walk through a test method's code, and the test assembly's own code it reaches, ended.</summary>
internal enum CodeWalkOutcome
{
    /// <summary>The walk found what it looked for.</summary>
    Found = 0,

    /// <summary>The walk read all the code the test reaches and did not find it.</summary>
    NotFound = 1,

    /// <summary>The walk could not tell: the test's class or method is not in the assembly, or the walk hit its bound.</summary>
    Undetermined = 2,
}
