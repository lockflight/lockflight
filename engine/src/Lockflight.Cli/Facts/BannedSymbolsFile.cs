namespace Lockflight.Cli.Facts;

/// <summary>A tracked <c>BannedSymbols.txt</c> and the symbol ids it lists (<c>P:System.DateTime.UtcNow</c>), in file order.</summary>
internal sealed record BannedSymbolsFile(string File, IReadOnlyList<string> Symbols);
