namespace Lockflight.Cli.Gates.Affected;

/// <summary>
/// One project of a solution's project-reference graph, read from its project file.
/// </summary>
/// <param name="File">The project file's full path.</param>
/// <param name="SolutionEntry">The path as the solution lists it; null for a project only another project references.</param>
/// <param name="References">The full paths of the project files it references (<c>ProjectReference</c>).</param>
/// <param name="ExtraInputs">
/// Files and directories outside its own directory that it includes (a linked source file, an imported shared project): a change
/// there changes this project too.
/// </param>
/// <param name="DeclaresTests">Its project file names a test framework or declares a test project.</param>
internal sealed record GraphProject(string File, string? SolutionEntry, IReadOnlyList<string> References, IReadOnlyList<string> ExtraInputs, bool DeclaresTests)
{
    public string Directory => Path.GetDirectoryName(File)!;

    public string Name => Path.GetFileName(File);
}
