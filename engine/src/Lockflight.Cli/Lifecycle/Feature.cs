namespace Lockflight.Cli.Lifecycle;

/// <summary>A Spec Kit feature: its directory under <c>specs/</c> and the matching Lockflight record directory.</summary>
internal sealed record Feature(string Id, string SpecDirectory, string RecordDirectory)
{
    public string SpecPath => Path.Combine(SpecDirectory, "spec.md");

    public string PlanPath => Path.Combine(SpecDirectory, "plan.md");

    public string TasksPath => Path.Combine(SpecDirectory, "tasks.md");
}
