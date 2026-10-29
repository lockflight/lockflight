using Lockflight.Cli.Hosting;

namespace Lockflight.Cli.Evidence.Blocked;

/// <summary>
/// <c>lockflight blocked validate &lt;file&gt;</c>: checks a <c>lockflight.blocked/v1</c> report against its contract, as a crew skill
/// or an outside tool checks what it wrote. Read only; exit 0 valid, 1 with each problem listed, 4 when the file cannot be read.
/// </summary>
internal sealed class BlockedValidateCommand(CliContext context) : ICommand
{
    public async Task<ExitCode> RunAsync(CommandArguments arguments, CancellationToken cancellationToken)
    {
        string path = Path.GetFullPath(arguments.Rest[0], context.WorkingDirectory);
        if (!File.Exists(path))
        {
            await context.Error.WriteLineAsync($"lockflight: no file at {path}");
            return ExitCode.Config;
        }

        BlockedReading reading = BlockedReportReader.Read(await File.ReadAllTextAsync(path, cancellationToken));
        foreach (string problem in reading.Problems)
        {
            await context.Output.WriteLineAsync($"{arguments.Rest[0]}: {problem}");
        }

        if (!reading.Valid)
        {
            await context.Output.WriteLineAsync($"{arguments.Rest[0]}: not a valid {BlockedReport.Schema} report");
            return ExitCode.Fail;
        }

        BlockedReport report = reading.Report!;
        await context.Output.WriteLineAsync(
            $"{arguments.Rest[0]}: valid {BlockedReport.Schema} ({report.CodeName}, {report.Needs.Count} need{(report.Needs.Count == 1 ? string.Empty : "s")}, {report.Questions.Count} question{(report.Questions.Count == 1 ? string.Empty : "s")})");
        return ExitCode.Pass;
    }
}
