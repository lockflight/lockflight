namespace Lockflight.Cli.Tests.Gates.WindowsTargets;

/// <summary>A scratch solution shaped like a WPF client with an ASP.NET Core server, each with a test project.</summary>
internal sealed class DesktopSolution : IDisposable
{
    public const string WindowsProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0-windows</TargetFramework>
            <UseWPF>true</UseWPF>
          </PropertyGroup>
        </Project>
        """;

    public const string ServerProject = """
        <Project Sdk="Microsoft.NET.Sdk.Web">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    public const string WindowsTestProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0-windows</TargetFramework>
            <UseWPF>true</UseWPF>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="xunit" Version="2.9.3" />
          </ItemGroup>
        </Project>
        """;

    public const string ServerTestProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net8.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="xunit" Version="2.9.3" />
          </ItemGroup>
        </Project>
        """;

    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-desktop-");

    public DesktopSolution(bool withServerTests = true)
    {
        List<string> entries = [@"src\Desk.Client\Desk.Client.csproj", @"src\Desk.Server\Desk.Server.csproj", @"tests\Desk.Client.Tests\Desk.Client.Tests.csproj"];
        Write("src/Desk.Client/Desk.Client.csproj", WindowsProject);
        Write("src/Desk.Server/Desk.Server.csproj", ServerProject);
        Write("tests/Desk.Client.Tests/Desk.Client.Tests.csproj", WindowsTestProject);
        if (withServerTests)
        {
            Write("tests/Desk.Server.Tests/Desk.Server.Tests.csproj", ServerTestProject);
            entries.Add(@"tests\Desk.Server.Tests\Desk.Server.Tests.csproj");
        }

        WriteSolution(entries);
    }

    public string Root => directory.FullName;

    public string SolutionFile => Path.Combine(Root, "Desk.sln");

    public void Write(string relativePath, string content)
    {
        string path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void WriteSolution(IEnumerable<string> entries) =>
        Write("Desk.sln", "Microsoft Visual Studio Solution File, Format Version 12.00\n" + string.Concat(entries.Select((entry, index) =>
            $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{Path.GetFileNameWithoutExtension(entry)}\", \"{entry}\", \"{{00000000-0000-0000-0000-00000000000{index}}}\"\nEndProject\n")));

    public void Dispose() => TempDirectories.DeleteRecursively(directory);
}
