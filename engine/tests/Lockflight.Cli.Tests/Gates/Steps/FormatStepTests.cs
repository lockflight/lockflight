using Lockflight.Cli.Config;
using Lockflight.Cli.Gates;
using Lockflight.Cli.Gates.Steps;
using Lockflight.Cli.Processes;

namespace Lockflight.Cli.Tests.Gates.Steps;

// These run the SDK's dotnet format on a scratch repository whose .editorconfig asks for CRLF while git holds LF files, as on Parlot.
public sealed class FormatStepTests : IDisposable
{
    private const string Original = "namespace Demo;\n\npublic class Calculator\n{\n    public int Add(int a, int b) => a + b;   \n\n    public int Sub(int a, int b) => a - b;\n}\n";

    private readonly DirectoryInfo repository = Directory.CreateTempSubdirectory("lockflight-format-step-");

    [Fact]
    public async Task ExistingDebtAndLineEndingsOutsideTheChangeDoNotFailTheStep()
    {
        await InitRepositoryAsync();
        Write("src/Calculator.cs", Original.Replace("a - b;", "a - b - 0;", StringComparison.Ordinal));

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.Pass, step.Verdict);
        Assert.Contains("end-of-line-only differences not attributed", step.Detail, StringComparison.Ordinal);
        Assert.Contains("not judged", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BadIndentationOnAChangedLineFailsTheStep()
    {
        await InitRepositoryAsync();
        Write("src/Calculator.cs", Original.Replace("    public int Sub(int a, int b) => a - b;", "  public int Sub(int a, int b) => a - b;", StringComparison.Ordinal));

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.Contains("src/Calculator.cs:", step.Detail, StringComparison.Ordinal);
        Assert.Contains("WHITESPACE", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChangeWithoutCSharpLinesIsNotApplicable()
    {
        await InitRepositoryAsync();
        Write("README.md", "changed\n");

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.NotApplicable, step.Verdict);
        Assert.Equal("no changed C# lines", step.Detail);
    }

    [Fact]
    public async Task SoManyChangedFilesThatTheCommandLineWouldBeTooLongAreStillChecked()
    {
        await InitRepositoryAsync();
        string longFolder = $"src/{new string('n', 120)}";
        for (int index = 0; index < 100; index++)
        {
            Write($"{longFolder}/Generated{index:D3}.cs", $"namespace Demo;\r\n\r\npublic class Generated{index:D3}\r\n{{\r\n}}\r\n");
        }

        Write($"{longFolder}/Bad.cs", "namespace Demo;\r\n\r\npublic class Bad\r\n{\r\n  public int X;\r\n}\r\n");

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.Contains("Bad.cs:5", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExistingByteOrderMarkIsNotBlamedOnAnEditInsideTheFile()
    {
        await InitRepositoryAsync();
        Write(".editorconfig", "root = true\n\n[*.cs]\ncharset = utf-8\nindent_style = space\nindent_size = 4\n");
        Write("src/Calculator.cs", "﻿" + Original.Replace("   \n", "\n", StringComparison.Ordinal));
        await CommitAllAsync();
        Write("src/Calculator.cs", "﻿" + Original.Replace("   \n", "\n", StringComparison.Ordinal).Replace("a - b;", "a - b - 0;", StringComparison.Ordinal));

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.Pass, step.Verdict);
        Assert.Contains("1 file encoding differences", step.Detail, StringComparison.Ordinal);
        Assert.Equal("1 changed C# files (format)", step.Scope);
    }

    [Fact]
    public async Task ANewFileWithAByteOrderMarkTheCharsetForbidsFails()
    {
        await InitRepositoryAsync();
        Write(".editorconfig", "root = true\n\n[*.cs]\ncharset = utf-8\nindent_style = space\nindent_size = 4\n");
        await CommitAllAsync();
        Write("src/Added.cs", "﻿namespace Demo;\n\npublic class Added\n{\n}\n");

        GateStep step = await RunStepAsync();

        Assert.Equal(Verdict.Fail, step.Verdict);
        Assert.Contains("src/Added.cs:1 (CHARSET)", step.Detail, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(repository);

    private async Task CommitAllAsync()
    {
        await GitAsync(["add", "-A"]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "change"]);
    }

    private async Task<GateStep> RunStepAsync()
    {
        GateRunContext run = new(repository.FullName, repository.FullName, GateProfile.Edit, GateSettings.Default, BaseRef: null, TimeProvider.System);
        return await FormatStep.RunAsync(run, TestContext.Current.CancellationToken);
    }

    private async Task InitRepositoryAsync()
    {
        await GitAsync(["init", "-q"]);
        await GitAsync(["config", "core.autocrlf", "false"]);
        Write(".editorconfig", "root = true\n\n[*.cs]\nend_of_line = crlf\nindent_style = space\nindent_size = 4\n");
        Write("README.md", "tracked\n");
        Write("src/Calculator.cs", Original);
        await GitAsync(["add", "."]);
        await GitAsync(["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "init"]);
    }

    private void Write(string relativePath, string content)
    {
        string path = Path.Combine(repository.FullName, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private Task<ProcessResult> GitAsync(string[] arguments) =>
        ProcessRunner.RunAsync("git", arguments, repository.FullName, TestContext.Current.CancellationToken);
}
