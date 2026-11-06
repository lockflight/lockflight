using Lockflight.Cli.Doctor;
using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Tests.Doctor;

public sealed class GitBashTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-git-bash-");

    [Fact]
    public void TheConfiguredGitBashComesFirst()
    {
        string configured = Touch("custom", Executable("bash"));
        Touch(Path.Combine("Git", "cmd"), Executable("git"));
        Touch(Path.Combine("Git", "bin"), Executable("bash"));

        string? found = GitBash.Find(EnvironmentReader.Of(new Dictionary<string, string>
        {
            [EnvironmentVariables.GitBashPath] = configured,
            [EnvironmentVariables.Path] = Path.Combine(root.FullName, "Git", "cmd"),
        }));

        Assert.Equal(configured, found);
    }

    [Fact]
    public void TheBashOfTheGitForWindowsOnPathIsFoundAsAFullPath()
    {
        Touch(Path.Combine("Git", "cmd"), Executable("git"));
        string bash = Touch(Path.Combine("Git", "bin"), Executable("bash"));

        string? found = GitBash.Find(EnvironmentReader.Of(new Dictionary<string, string>
        {
            [EnvironmentVariables.Path] = Path.Combine(root.FullName, "Git", "cmd"),
        }));

        Assert.Equal(bash, found);
    }

    [Fact]
    public void GitForWindowsInProgramFilesIsFoundWithoutGitOnPath()
    {
        string bash = Touch(Path.Combine("Program Files", "Git", "bin"), Executable("bash"));

        string? found = GitBash.Find(EnvironmentReader.Of(new Dictionary<string, string>
        {
            [EnvironmentVariables.ProgramFiles] = Path.Combine(root.FullName, "Program Files"),
        }));

        Assert.Equal(bash, found);
    }

    [Fact]
    public void WslBashUnderTheWindowsFolderIsNotTakenForGitBash()
    {
        Touch(Path.Combine("Windows", "System32"), Executable("bash"));

        string? found = GitBash.Find(EnvironmentReader.Of(new Dictionary<string, string>
        {
            [EnvironmentVariables.SystemRoot] = Path.Combine(root.FullName, "Windows"),
            [EnvironmentVariables.Path] = Path.Combine(root.FullName, "Windows", "System32"),
        }));

        Assert.Null(found);
    }

    [Fact]
    public void ABashOnPathAfterTheWindowsFolderIsFound()
    {
        Touch(Path.Combine("Windows", "System32"), Executable("bash"));
        string bash = Touch(Path.Combine("tools", "bin"), Executable("bash"));

        string? found = GitBash.Find(EnvironmentReader.Of(new Dictionary<string, string>
        {
            [EnvironmentVariables.WindowsDirectory] = Path.Combine(root.FullName, "Windows") + Path.DirectorySeparatorChar,
            [EnvironmentVariables.Path] = string.Join(Path.PathSeparator, Path.Combine(root.FullName, "Windows", "System32"), Path.Combine(root.FullName, "tools", "bin")),
        }));

        Assert.Equal(bash, found);
    }

    [Fact]
    public void WithoutGitBashNothingIsFound() =>
        Assert.Null(GitBash.Find(EnvironmentReader.Of(new Dictionary<string, string> { [EnvironmentVariables.Path] = root.FullName })));

    [Fact]
    public async Task NoGitBashFailsWithItsFix()
    {
        DoctorCheck check = await ReadinessChecks.CheckGitBashAsync(root.FullName, null, TestContext.Current.CancellationToken);

        Assert.Equal(CheckStatus.Fail, check.Status);
        Assert.Contains("Git for Windows", check.Fix, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private static string Executable(string name) => OperatingSystem.IsWindows() ? $"{name}.exe" : name;

    private string Touch(string folder, string name)
    {
        string directory = Path.Combine(root.FullName, folder);
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, name);
        File.WriteAllText(file, string.Empty);
        return file;
    }
}
