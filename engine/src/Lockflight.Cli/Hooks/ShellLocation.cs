namespace Lockflight.Cli.Hooks;

/// <summary>
/// A directory a command may be running in. <see cref="Directory"/> null is the hook's working directory. A floating location
/// sits below a directory the scanner could not follow (<c>cd "$X"</c>, <c>cd ~</c>, <c>popd</c>); its
/// <see cref="Directory"/> is then the '/'-separated part known below that unknown directory ("" for the directory itself).
/// </summary>
internal sealed record ShellLocation(string? Directory, bool Floating)
{
    public static ShellLocation Start { get; } = new(null, Floating: false);

    public static ShellLocation Unknown { get; } = new(string.Empty, Floating: true);
}
