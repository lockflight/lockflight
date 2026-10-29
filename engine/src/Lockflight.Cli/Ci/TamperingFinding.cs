namespace Lockflight.Cli.Ci;

/// <summary>
/// Code in a test assembly that could tamper with the test host's results: the assembly, what kind of code (<c>module-initializer</c>,
/// <c>process-exit</c>, <c>exit</c>, <c>process-start</c>, <c>file-write</c>, <c>runner-reflection</c>, <c>ci-environment</c>), the
/// method it is in, and the member it uses. <see cref="Id"/> is how a baseline names an accepted one.
/// </summary>
internal sealed record TamperingFinding(string Assembly, string Kind, string Location, string Member)
{
    public string Id => $"{Assembly}|{Kind}|{Location}|{Member}";
}
