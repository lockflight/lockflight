using Lockflight.Cli.Linking;

namespace Lockflight.Cli.Tests.Linking;

public sealed class ExecutableReplacementTests : IDisposable
{
    private readonly DirectoryInfo bin = Directory.CreateTempSubdirectory("lockflight-bin-");

    [Fact]
    public void TheReplacementTakesTheTargetsPlace()
    {
        string target = Write("lockflight", "old engine");
        string replacement = Write("lockflight.new", "new engine");

        ExecutableReplacement.Replace(replacement, target);

        Assert.Equal("new engine", File.ReadAllText(target));
        Assert.False(File.Exists(replacement));
    }

    [Fact]
    public void AReplacementWithNoTargetYetIsMovedIntoPlace()
    {
        string target = Path.Combine(bin.FullName, "lockflight");
        string replacement = Write("lockflight.new", "new engine");

        ExecutableReplacement.Replace(replacement, target);

        Assert.Equal("new engine", File.ReadAllText(target));
    }

    [Fact]
    public void EnginesSetAsideByAnEarlierReplacementAreRemoved()
    {
        string target = Write("lockflight", "old engine");
        Write("lockflight.old-abc123", "an engine that was running");
        string replacement = Write("lockflight.new", "new engine");

        ExecutableReplacement.Replace(replacement, target);

        Assert.Equal(["lockflight"], Directory.GetFiles(bin.FullName).Select(Path.GetFileName));
    }

    private string Write(string name, string content)
    {
        string path = Path.Combine(bin.FullName, name);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose() => TempDirectories.DeleteRecursively(bin);
}
