using Lockflight.Cli.Processes.Survivors;

namespace Lockflight.Cli.Tests.Processes.Survivors;

public sealed class ProcStatTests
{
    [Fact]
    public void AStatLineIsReadAfterTheLastParenthesisOfTheCommandName()
    {
        ProcStat? stat = ProcStat.Parse("4711 (sh -c (evil) 1 2) S 4700 4711 4711 0 -1 4194560 120 0 0 0 0 0 0 0 20 0 1 0 98765 2392064 200 18446744073709551615");

        Assert.Equal(new ProcStat(4711, 4700, 'S', 98765), stat);
    }

    [Fact]
    public void AZombieIsRecognised() =>
        Assert.True(ProcStat.Parse("12 (sleep) Z 1 12 12 0 -1 0 0 0 0 0 0 0 0 0 20 0 1 0 555 0 0 0")!.IsZombie);

    [Fact]
    public void ALineThatIsNotAStatLineIsRefused()
    {
        Assert.Null(ProcStat.Parse(string.Empty));
        Assert.Null(ProcStat.Parse("(sleep) S 1"));
        Assert.Null(ProcStat.Parse("12 (sleep) S 1 2 3"));
    }

    [Fact]
    public void DescendantsFollowEveryChainOfParentsFromTheRootAndNothingElse()
    {
        ProcStat[] table =
        [
            new(1, 0, 'S', 1),
            new(100, 1, 'S', 2),   // the engine
            new(200, 100, 'S', 3), // a build
            new(300, 200, 'S', 4), // its worker
            new(400, 100, 'Z', 5), // an orphan re-parented to the engine, exited
            new(500, 400, 'S', 6),
            new(600, 1, 'S', 7),   // someone else's
        ];

        IReadOnlyList<ProcStat> descendants = ProcStat.Descendants(table, 100);

        Assert.Equal([200, 300, 400, 500], descendants.Select(process => process.Pid).Order());
    }
}
