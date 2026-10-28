using System.Text;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Tests.Goldens;

namespace Lockflight.Cli.Tests.Hooks;

/// <summary>
/// The routing of a simple command to the writer that reads its targets, pinned per dialect: every program a writer knows
/// (PowerShell aliases included, which overlap the POSIX programs), a few that write nothing, behind <c>xargs</c> and fed by a
/// pipeline.
/// </summary>
public sealed class CommandWritersTruthTableTests
{
    private static readonly string[] Programs =
    [
        "git", "dotnet",
        "cp", "mv", "install", "ln", "rsync",
        "sed", "perl",
        "sort", "uniq", "awk", "gawk", "yq",
        "robocopy", "xcopy",
        "tar", "bsdtar", "unzip", "patch",
        "rm", "unlink", "shred", "truncate", "touch", "chmod", "chown", "chgrp", "tee", "dd", "find", "curl", "wget",
        "del", "erase", "ri", "rd", "rmdir", "copy", "cpi", "move", "mi", "ren", "rni", "sc", "ac", "clc", "ni", "si", "cli", "epcsv", "iwr", "irm",
        "set-content", "add-content", "clear-content", "out-file", "tee-object", "set-item", "new-item", "clear-item", "export-csv", "export-clixml",
        "remove-item", "copy-item", "move-item", "rename-item", "invoke-webrequest", "invoke-restmethod", "expand-archive",
        "cat", "echo", "ls", "grep", "make",
    ];

    private static readonly string[][] Arguments =
    [
        ["a", "b"],
        ["-o", "out", "in"],
        ["-i", "x"],
        ["-r", "dir"],
        ["-Path", "p", "-Destination", "d"],
        ["of=o", "if=i"],
        [".", "-name", "*.tmp", "-delete"],
        [".", "-exec", "rm", "{}", ";"],
        ["checkout", "--", "f"],
        ["format", "--include", "f.cs"],
    ];

    [Fact]
    public void EveryProgramIsRoutedToTheSameWriterInEachDialect()
    {
        StringBuilder rendered = new();
        foreach (ShellDialect dialect in (ShellDialect[])[ShellDialect.Posix, ShellDialect.PowerShell])
        {
            foreach (string program in Programs)
            {
                foreach (string[] arguments in Arguments)
                {
                    Render(rendered, dialect, [program, .. arguments], piped: false);
                }

                Render(rendered, dialect, ["xargs", program], piped: false);
                Render(rendered, dialect, [program], piped: true);
            }
        }

        GoldenFiles.Verify("command-writers", rendered.ToString());
    }

    private static void Render(StringBuilder rendered, ShellDialect dialect, string[] words, bool piped)
    {
        ShellCommand parsed = ShellCommand.Parse(words) ?? throw new InvalidOperationException($"'{string.Join(' ', words)}' does not parse");
        ShellCommand command = parsed with { PipedInput = piped };
        CommandWrites writes = CommandWriters.Of(command, dialect);
        rendered.Append($"{dialect} {(piped ? "| " : string.Empty)}{string.Join(' ', words)}: writer={CommandWriters.IsWriter(command, dialect)}");
        Append(rendered, "files", writes.Files);
        Append(rendered, "trees", writes.Trees);
        Append(rendered, "sources", writes.SourceTrees);
        Append(rendered, "patterns", writes.PatternFiles);
        if (writes.Hidden is string hidden)
        {
            rendered.Append($" hidden={hidden}");
        }

        rendered.Append('\n');
    }

    private static void Append(StringBuilder rendered, string label, IReadOnlyList<string> paths)
    {
        if (paths.Count > 0)
        {
            rendered.Append($" {label}=[{string.Join(", ", paths)}]");
        }
    }
}
