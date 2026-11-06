namespace Lockflight.Cli.Tests.Projects;

/// <summary>Writes the test projects of a solution with a desktop client and a server into a directory.</summary>
internal static class DesktopProjects
{
    public const string WpfTests = "tests/Client.Tests/Client.Tests.csproj";
    public const string ServerTests = "tests/Server.Tests/Server.Tests.csproj";

    /// <summary>A WPF test project on <c>net8.0-windows</c>.</summary>
    public static string WriteWpfTests(string root) =>
        Write(root, WpfTests, "<PropertyGroup><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF></PropertyGroup>");

    /// <summary>A server test project on <c>net8.0</c>.</summary>
    public static string WriteServerTests(string root) =>
        Write(root, ServerTests, "<PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>");

    public static string Write(string root, string relativePath, string properties, string package = "xunit")
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"<Project Sdk=\"Microsoft.NET.Sdk\">\n  {properties}\n  <ItemGroup><PackageReference Include=\"{package}\" Version=\"2.9.3\" /></ItemGroup>\n</Project>\n");
        return path;
    }
}
