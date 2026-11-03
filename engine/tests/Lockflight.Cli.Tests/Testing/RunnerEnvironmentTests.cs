using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class RunnerEnvironmentTests
{
    [Fact]
    public void OnlyTheVariablesThatChangeHowDotnetCodeBehavesAreDescribedInNameOrder()
    {
        Dictionary<string, string?> inherited = new(StringComparer.Ordinal)
        {
            ["PATH"] = "/usr/bin",
            ["TZ"] = "UTC",
            ["LANG"] = "en_US.UTF-8",
            ["LC_ALL"] = "C",
            ["DOTNET_ROOT"] = "/dotnet",
            ["HOME"] = "/home/me",
        };
        Dictionary<string, string?> overrides = new(StringComparer.Ordinal) { ["ASPNETCORE_ENVIRONMENT"] = "Testing", ["LC_ALL"] = null };

        Assert.Equal(
            "ASPNETCORE_ENVIRONMENT=Testing DOTNET_ROOT=/dotnet LANG=en_US.UTF-8 TZ=UTC",
            RunnerEnvironment.Describe(inherited, overrides));
    }

    [Fact]
    public void AValueWhoseNameLooksSecretIsRedacted() =>
        Assert.Equal(
            "ASPNETCORE_Kestrel__Certificates__Default__Password=<redacted> DOTNET_ENVIRONMENT=Testing",
            RunnerEnvironment.Describe(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ASPNETCORE_Kestrel__Certificates__Default__Password"] = "hunter2",
                ["DOTNET_ENVIRONMENT"] = "Testing",
            }));

    [Fact]
    public void NoRelevantVariableIsDescribedAsNone() =>
        Assert.Equal("none", RunnerEnvironment.Describe(new Dictionary<string, string?>(StringComparer.Ordinal) { ["PATH"] = "/usr/bin" }));
}
