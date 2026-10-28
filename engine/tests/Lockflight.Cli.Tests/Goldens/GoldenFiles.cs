using System.Runtime.CompilerServices;

namespace Lockflight.Cli.Tests.Goldens;

/// <summary>
/// Compares text a test renders with a file checked in next to this class, so a refactoring that changes an exit code, a block
/// code or a message fails with the whole difference in view. <c>LOCKFLIGHT_UPDATE_GOLDENS=1</c> rewrites the files instead;
/// the diff is then reviewed like code.
/// </summary>
internal static class GoldenFiles
{
    private const string UpdateVariable = "LOCKFLIGHT_UPDATE_GOLDENS";

    public static void Verify(string name, string actual, [CallerFilePath] string callerFile = "")
    {
        string path = Path.Combine(Path.GetDirectoryName(PathOfThisFile())!, $"{name}.txt");
        string normalized = actual.ReplaceLineEndings("\n").TrimEnd('\n') + "\n";
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(path, normalized);
            return;
        }

        Assert.True(File.Exists(path), $"{path} does not exist; run the tests of {Path.GetFileName(callerFile)} with {UpdateVariable}=1 to write it");
        string expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        Assert.Equal(expected, normalized);
    }

    private static string PathOfThisFile([CallerFilePath] string path = "") => path;
}
