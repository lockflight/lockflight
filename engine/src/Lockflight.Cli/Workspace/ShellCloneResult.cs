namespace Lockflight.Cli.Workspace;

/// <summary>Whether the workspace shell was cloned, and when not, why (with any token removed from the message).</summary>
internal sealed record ShellCloneResult(bool Succeeded, string? Error)
{
    public static ShellCloneResult Cloned { get; } = new(Succeeded: true, Error: null);

    public static ShellCloneResult Failed(string error) => new(Succeeded: false, error);
}
