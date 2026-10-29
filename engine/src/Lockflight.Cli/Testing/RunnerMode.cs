namespace Lockflight.Cli.Testing;

/// <summary>
/// How a test application is listed and run: xUnit v3 natively or through MTP options (projects that set
/// <c>UseMicrosoftTestingPlatformRunner</c> accept only MTP options), xUnit v2 through VSTest, and NUnit through VSTest or
/// Microsoft.Testing.Platform with its TRX report.
/// </summary>
internal enum RunnerMode
{
    /// <summary>Not probed yet.</summary>
    Unknown = 0,

    /// <summary>xUnit's in-process console runner: <c>-list</c>, <c>-method</c>, <c>-result-xml</c> (<c>-xml</c> before 4.0).</summary>
    Native = 1,

    /// <summary>VSTest (xUnit v2): tests listed from assembly metadata, run with <c>dotnet test &lt;dll&gt; --filter</c>, results from TRX.</summary>
    VSTest = 3,

    /// <summary>Microsoft.Testing.Platform with xUnit's extension options: <c>--xunit-list</c>, <c>--filter-method</c>, <c>--report-xunit-xml</c>.</summary>
    Mtp = 2,

    /// <summary>NUnit through VSTest: tests listed from assembly metadata, run with <c>dotnet test &lt;dll&gt; --settings</c> (an NUnit <c>Where</c>), results from TRX.</summary>
    NUnit = 4,

    /// <summary>NUnit on Microsoft.Testing.Platform: listed from metadata, run with <c>dotnet &lt;dll&gt; --settings … --report-trx</c>.</summary>
    NUnitMtp = 5,
}
