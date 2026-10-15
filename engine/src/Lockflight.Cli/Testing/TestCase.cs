namespace Lockflight.Cli.Testing;

/// <summary>
/// A test method from the runner's inventory. Theories are one case per method (rows are not pre-enumerated), so a new data row
/// on an existing theory is not a new test — a documented limit of red/green. <see cref="IsExplicit"/> is xUnit v3's
/// <c>Explicit = true</c>: the test is listed but a normal run never executes it (its outcome is NotRun).
/// </summary>
internal sealed record TestCase(string Assembly, string Class, string Method, IReadOnlyList<string> Requirements, string? SkipReason, bool IsExplicit = false)
{
    /// <summary>Identity across test projects: assembly, class (nested as <c>Outer+Inner</c>) and method.</summary>
    public string Key => $"{Assembly}:{Class}.{Method}";

    /// <summary>The fully qualified method name the runner's <c>-method</c> filter takes.</summary>
    public string MethodFilter => $"{Class}.{Method}";
}
