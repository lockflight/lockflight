using Lockflight.Cli.Gates;

namespace Lockflight.Cli.Testing;

/// <summary>
/// Lists an NUnit assembly's tests from its metadata, once the platform it would run on can write the TRX the run is
/// read from: a Microsoft.Testing.Platform application needs the TRX report extension in its output, a VSTest run needs VSTest's
/// test host (<c>Microsoft.NET.Test.Sdk</c>). Without it the listing says what to add, so the probe can try the other platform.
/// </summary>
internal static class TrxTestListing
{
    private const string TrxExtension = "Microsoft.Testing.Extensions.TrxReport.dll";

    private const string TestHost = "testhost.dll";

    public static TestListing List(TestApp app, TestRunnerKind platform, TestFrameworkKind framework, RunnerMode mode)
    {
        string output = Path.GetDirectoryName(app.AssemblyPath) ?? string.Empty;
        string name = framework == TestFrameworkKind.NUnit ? "NUnit" : "The test framework";
        if (platform == TestRunnerKind.MicrosoftTestingPlatform && !File.Exists(Path.Combine(output, TrxExtension)))
        {
            return TestListing.Unavailable(
                $"{name} on Microsoft.Testing.Platform is run through its TRX report: add a PackageReference to Microsoft.Testing.Extensions.TrxReport to the test project (or Microsoft.NET.Test.Sdk to run it on VSTest)");
        }

        if (platform == TestRunnerKind.VSTest && !File.Exists(Path.Combine(output, TestHost)))
        {
            return TestListing.Unavailable($"{name} on VSTest needs VSTest's test host: add a PackageReference to Microsoft.NET.Test.Sdk to the test project");
        }

        try
        {
            return TestListing.Of(AssemblyTestMetadata.Read(app.AssemblyPath, framework), mode);
        }
        catch (Exception exception) when (exception is BadImageFormatException or IOException or InvalidOperationException)
        {
            return TestListing.NotListed;
        }
    }
}
