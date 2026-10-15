using System.Text.Json;

namespace Lockflight.Cli.Gates;

/// <summary>
/// The test platform <c>dotnet test</c> uses in a directory, decided the way the .NET SDK decides it: the nearest
/// <c>global.json</c> at or above the directory, its <c>test.runner</c> value.
/// </summary>
internal static class TestRunnerDetection
{
    private const string MicrosoftTestingPlatform = "Microsoft.Testing.Platform";

    public static TestRunnerKind For(string directory)
    {
        for (DirectoryInfo? current = new(Path.GetFullPath(directory)); current is not null; current = current.Parent)
        {
            string globalJson = Path.Combine(current.FullName, "global.json");
            if (File.Exists(globalJson))
            {
                return RunnerIn(globalJson);
            }
        }

        return TestRunnerKind.VSTest;
    }

    private static TestRunnerKind RunnerIn(string globalJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(globalJson), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("test", out JsonElement test)
                && test.ValueKind == JsonValueKind.Object
                && test.TryGetProperty("runner", out JsonElement runner)
                && runner.ValueKind == JsonValueKind.String
                && string.Equals(runner.GetString(), MicrosoftTestingPlatform, StringComparison.OrdinalIgnoreCase)
                ? TestRunnerKind.MicrosoftTestingPlatform
                : TestRunnerKind.VSTest;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // An unreadable global.json breaks dotnet test itself; the test step then reports that run's failure.
            return TestRunnerKind.VSTest;
        }
    }
}
