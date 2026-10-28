using Lockflight.Cli.Adoption;

namespace Lockflight.Cli.Tests.Adoption;

public sealed class SetupInventoryClaudeMdTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-inventory-");

    [Fact]
    public void AMissingClaudeMdIsCreatedWithoutATokenCount()
    {
        AdoptionItem item = ClaudeMdItem();

        Assert.Equal(AdoptionAction.Add, item.Action);
        Assert.Equal("there is none yet; it is created with a short Lockflight block", item.Reason);
    }

    [Fact]
    public void AnEmptyClaudeMdGetsTheBlockWithoutATokenCount()
    {
        File.WriteAllText(Path.Combine(root.FullName, "CLAUDE.md"), "\n  \n");

        AdoptionItem item = ClaudeMdItem();

        Assert.Equal(AdoptionAction.Add, item.Action);
        Assert.Equal("the file is empty; a short Lockflight block is added", item.Reason);
    }

    [Fact]
    public void AShortClaudeMdCountsAtLeastOneToken()
    {
        File.WriteAllText(Path.Combine(root.FullName, "CLAUDE.md"), "# A\n");

        AdoptionItem item = ClaudeMdItem();

        Assert.Equal(AdoptionAction.Add, item.Action);
        Assert.EndsWith($"the rest is kept; about 1 token of the {SetupInventory.ClaudeMdTokenBudget} budget", item.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ALongerClaudeMdReportsItsSizeInTokens()
    {
        File.WriteAllText(Path.Combine(root.FullName, "CLAUDE.md"), new string('x', 400));

        AdoptionItem item = ClaudeMdItem();

        Assert.Contains($"about 100 tokens of the {SetupInventory.ClaudeMdTokenBudget} budget", item.Reason, StringComparison.Ordinal);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(root);

    private AdoptionItem ClaudeMdItem() =>
        Assert.Single(SetupInventory.Build(root.FullName, null, null, At).Items, item => item.Area == AdoptionArea.ClaudeMd);
}
