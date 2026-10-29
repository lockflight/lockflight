using System.Text;
using Lockflight.Cli.Config;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Tests.Goldens;

namespace Lockflight.Cli.Tests.Hosting;

/// <summary>
/// The command line's dispatch, pinned: help anywhere, the version alias, arity, unknown commands and verbs, and the refusal of
/// human-only commands inside a Claude Code session, each with its exit code and its exact output.
/// </summary>
public sealed class DispatchGoldenTests : IDisposable
{
    private const string UsageMarker = "<usage>";

    private static readonly string[][] Anywhere =
    [
        [],
        ["--help"],
        ["-h"],
        ["gate", "run", "--help"],
        ["nonsense", "-h"],
        ["version"],
        ["--version"],
        ["version", "x"],
        ["--version", "x"],
        ["link", "x"],
        ["statusline", "x"],
        ["hook"],
        ["hook", "a", "b"],
        ["redgreen"],
        ["redgreen", "xyz"],
        ["approve"],
        ["approve", "xyz"],
        ["baseline"],
        ["baseline", "xyz"],
        ["adopt"],
        ["adopt", "xyz"],
        ["gate"],
        ["gate", "check"],
        ["docs"],
        ["docs", "x"],
        ["rules"],
        ["phase"],
        ["phase", "check"],
        ["ledger"],
        ["workspace"],
        ["review", "xyz"],
        ["trace", "x"],
        ["propose"],
        ["init", "x"],
        ["update", "x"],
        ["foo"],
        ["Gate", "run"],
    ];

    private static readonly string[][] InSession =
    [
        ["approve", "spec", "--help"],
        ["approve"],
        ["approve", "spec"],
        ["approve", "tasks", "--feature", "001-x"],
        ["approve", "xyz"],
        ["approve", "verify"],
        ["baseline"],
        ["baseline", "build"],
        ["baseline", "xyz"],
        ["redgreen", "reset"],
        ["redgreen", "amend", "A.B", "--reason", "why not"],
        ["redgreen", "xyz"],
        ["review", "dismiss", "0123", "--reason", "r"],
        ["adopt", "apply"],
        ["adopt", "undo"],
        ["adopt", "xyz"],
        ["update"],
        ["update", "--source", "feed"],
        ["foo"],
    ];

    private static readonly string[][] InProject =
    [
        ["redgreen", "xyz"],
        ["redgreen", "start", "extra"],
        ["phase", "check", "xyz"],
        ["phase", "check", "specify", "extra"],
        ["review", "dismiss"],
        ["redgreen", "amend"],
        ["approve", "spec", "--sign"],
        ["approve", "verify", "--feature", "a", "--base", "b"],
    ];

    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-dispatch-");

    [Fact]
    public async Task TheCommandLineIsDispatchedAsBefore()
    {
        StringBuilder rendered = new();
        string? usage = null;
        foreach (string[] args in Anywhere)
        {
            usage = await RenderAsync(rendered, args, new Dictionary<string, string>(), usage);
        }

        foreach (string[] args in InSession)
        {
            usage = await RenderAsync(rendered, args, new Dictionary<string, string> { [EnvironmentVariables.ClaudeCode] = "1" }, usage);
        }

        File.WriteAllText(Path.Combine(directory.FullName, Manifest.FileName), "version: 1\n");
        foreach (string[] args in InProject)
        {
            usage = await RenderAsync(rendered, args, new Dictionary<string, string>(), usage);
        }

        rendered.Append($"{UsageMarker}:\n{usage}\n");
        GoldenFiles.Verify("dispatch", rendered.ToString());
    }

    [Fact]
    public void TheCommandCatalogIsTheSameAsBefore()
    {
        StringBuilder rendered = new($"global: {string.Join(' ', CommandCatalog.GlobalOptions)}\n");
        foreach (CatalogedCommand command in CommandCatalog.All)
        {
            rendered.Append($"{command.Name}: [{string.Join(' ', command.Options)}]{(command.TakesArguments ? " arguments" : string.Empty)}\n");
        }

        GoldenFiles.Verify("command-catalog", rendered.ToString());
    }

    public void Dispose() => TempDirectories.DeleteRecursively(directory);

    private async Task<string?> RenderAsync(StringBuilder rendered, string[] args, Dictionary<string, string> environment, string? usage)
    {
        (ExitCode exitCode, string output, string error) = await TestConsole.RunAsync(args, string.Empty, directory.FullName, environment);
        bool printsUsage = output.StartsWith("lockflight — gated delivery engine", StringComparison.Ordinal);
        if (printsUsage)
        {
            Assert.True(usage is null || usage == output, "every help request prints the same text");
        }

        string session = environment.Count > 0 ? " (CLAUDECODE=1)" : string.Empty;
        rendered.Append($"$ lockflight {string.Join(' ', args)}{session}\n");
        rendered.Append($"exit: {(int)exitCode} {exitCode}\n");
        rendered.Append($"stdout: {(printsUsage ? UsageMarker : Normalize(output))}\n");
        rendered.Append($"stderr: {Normalize(error)}\n\n");
        return printsUsage ? output : usage;
    }

    private string Normalize(string text) => text
        .Replace(directory.FullName, "<dir>", StringComparison.Ordinal)
        .Replace(ToolPin.RunningVersion, "<version>", StringComparison.Ordinal);
}
