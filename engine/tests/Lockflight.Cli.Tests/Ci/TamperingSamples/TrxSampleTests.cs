namespace Lockflight.Cli.Tests.Ci.TamperingSamples;

/// <summary>An async test in a class whose name holds "Trx" writes into the fixture's folder: no finding. Scanned, never run.</summary>
internal sealed class TrxSampleTests(TempFolderOwner fixture)
{
    public async Task WriteReportAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        await File.WriteAllTextAsync(Path.Combine(fixture.Folder, "report.xml"), "<x/>", cancellationToken);
    }
}
