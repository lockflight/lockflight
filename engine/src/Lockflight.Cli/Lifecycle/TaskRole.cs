namespace Lockflight.Cli.Lifecycle;

/// <summary>What a task in tasks.md does for the test-first order of its user story.</summary>
internal enum TaskRole
{
    /// <summary>Production work; within a story it must come after every test task.</summary>
    Implementation = 0,

    /// <summary>Writes tests; within a story every test task comes before the implementation.</summary>
    Test = 1,

    /// <summary>Declares signatures whose bodies throw; it may come anywhere, typically before the tests it compiles against.</summary>
    Stub = 2,
}
