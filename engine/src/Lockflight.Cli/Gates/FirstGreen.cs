using System.Globalization;

namespace Lockflight.Cli.Gates;

/// <summary>
/// Time to first green: <c>init</c> records when it first ran in <c>.lockflight/local/first-green</c> (git-ignored), and the first
/// PASS of <c>lockflight gate</c> or a local <c>lockflight ci</c> after it says how long it took, once, and records when. The file
/// is local, so a CI run never reports it; losing or failing to read it only loses the report.
/// </summary>
internal sealed class FirstGreen(string projectRoot, TimeProvider time)
{
    public const string RelativePath = ".lockflight/local/first-green";

    private string FilePath => Path.Combine(projectRoot, RelativePath);

    /// <summary>Records when init ran, unless an earlier init already did.</summary>
    public async Task RecordInitAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                await File.WriteAllTextAsync(FilePath, Stamp(time.GetUtcNow()) + "\n", cancellationToken);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Without the record there is only no report.
        }
    }

    /// <summary>The line to show after a PASS when it is the first since init, or null; the PASS is recorded so it shows once.</summary>
    public async Task<string?> ReportPassAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            string[] lines = (await File.ReadAllTextAsync(FilePath, cancellationToken)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length != 1 || !DateTimeOffset.TryParse(lines[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset initialized))
            {
                return null;
            }

            DateTimeOffset now = time.GetUtcNow();
            await File.AppendAllTextAsync(FilePath, Stamp(now) + "\n", cancellationToken);
            return $"First green {Describe(now - initialized)} after init.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Stamp(DateTimeOffset moment) => moment.ToString("O", CultureInfo.InvariantCulture);

    private static string Describe(TimeSpan elapsed) => elapsed switch
    {
        { TotalMinutes: < 1 } => "in under a minute",
        { TotalHours: < 1 } => $"in {(int)elapsed.TotalMinutes} min",
        { TotalDays: < 1 } => $"in {(int)elapsed.TotalHours} h {elapsed.Minutes} min",
        _ => $"in {(int)elapsed.TotalDays} days",
    };
}
