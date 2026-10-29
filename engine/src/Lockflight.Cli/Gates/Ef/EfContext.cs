namespace Lockflight.Cli.Gates.Ef;

/// <summary>
/// One DbContext with migrations: its project file and directory (relative to the project root, '/'-separated), the context's name
/// as its model snapshot names it, the startup project <c>dotnet ef</c> needs (null when the project is its own), and the
/// directories of every project it references, directly or not, whose code can change the model.
/// </summary>
internal sealed record EfContext(string Project, string Directory, string Context, string? StartupProject, IReadOnlyList<string> ModelDirectories)
{
    /// <summary>The project and context as the step names them: <c>src/Shop.Data/Shop.Data.csproj (ShopContext)</c>.</summary>
    public string Describe() => $"{Project} ({Context})";
}
