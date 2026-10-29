using System.Text;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hosting;
using Lockflight.Cli.Tests.Goldens;

namespace Lockflight.Cli.Tests.Hosting;

/// <summary>
/// Which command lines are reserved for a person, pinned for every caller: the engine's own refusal (<c>IsReserved</c>), the
/// guard's search of shell words (<c>FindInWords</c>, which drops option words first, and <c>FindIn</c> on a whole command
/// line).
/// </summary>
public sealed class ReservationTruthTableTests
{
    private static readonly string[][] Extra =
    [
        [],
        ["approve"],
        ["approve", "xyz"],
        ["approve", "verify", "--base", "main"],
        ["approve", "--feature", "verify"],
        ["baseline"],
        ["baseline", "xyz"],
        ["update", "--source", "feed"],
        ["redgreen"],
        ["redgreen", "xyz"],
        ["redgreen", "--feature", "x", "reset"],
        ["review", "xyz"],
        ["adopt"],
        ["adopt", "xyz"],
        ["Approve", "spec"],
        ["gate", "run", "approve"],
    ];

    [Fact]
    public void TheReservedCommandsAreTheSameForEveryCaller()
    {
        StringBuilder rendered = new();
        IEnumerable<string[]> lines = CommandCatalog.All.Select(command => command.Words.ToArray()).Concat(Extra);
        foreach (string[] words in lines)
        {
            string line = string.Join(' ', words);
            rendered.Append($"{(line.Length == 0 ? "(nothing)" : line)}: ");
            rendered.Append($"reserved={HumanOnlyCommands.IsReserved(words)} ");
            rendered.Append($"words={HumanOnlyCommands.FindInWords(["lockflight", .. words], honorHelp: true) ?? "-"} ");
            rendered.Append($"shell={HumanOnlyCommands.FindIn($"dotnet lockflight {line}", ShellDialect.Posix) ?? "-"} ");
            rendered.Append($"powershell={HumanOnlyCommands.FindIn($".lockflight/bin/lockflight.exe {line}", ShellDialect.PowerShell) ?? "-"}\n");
        }

        GoldenFiles.Verify("reservation", rendered.ToString());
    }
}
