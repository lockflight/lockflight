using Lockflight.Cli.Testing;

namespace Lockflight.Cli.Tests.Testing;

public sealed class RunSettingsExtensionsTests : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("lockflight-runsettings-");

    [Fact]
    public void CollectorsAdapterPathsAndLoggersAreListedButTheCoverageCollectorIsNot()
    {
        string path = Write("""
            <RunSettings>
              <RunConfiguration><TestAdaptersPaths>adapters;more/adapters</TestAdaptersPaths></RunConfiguration>
              <DataCollectionRunSettings>
                <DataCollectors>
                  <DataCollector friendlyName="ResultRewriter" uri="datacollector://shop/rewriter" />
                  <DataCollector friendlyName="XPlat Code Coverage" />
                </DataCollectors>
              </DataCollectionRunSettings>
              <InProcDataCollectionRunSettings>
                <InProcDataCollectors>
                  <InProcDataCollector assemblyQualifiedName="Shop.Forge, Shop.Forge" codebase="forge/Shop.Forge.dll" />
                </InProcDataCollectors>
              </InProcDataCollectionRunSettings>
              <LoggerRunSettings><Loggers><Logger friendlyName="QuietPass" /></Loggers></LoggerRunSettings>
            </RunSettings>
            """);

        IReadOnlyList<(string Kind, string Identity)> found = RunSettingsExtensions.Read(path);

        Assert.Equal(
            [
                (RunSettingsExtensions.DataCollector, "ResultRewriter datacollector://shop/rewriter"),
                (RunSettingsExtensions.InProcessCollector, "Shop.Forge, Shop.Forge forge/Shop.Forge.dll"),
                (RunSettingsExtensions.Logger, "QuietPass"),
                (RunSettingsExtensions.AdapterPath, "adapters"),
                (RunSettingsExtensions.AdapterPath, "more/adapters"),
            ],
            found);
    }

    [Fact]
    public void ACoverageCollectorNameWithACodeBaseOfItsOwnIsListed()
    {
        string path = Write("""<RunSettings><DataCollectionRunSettings><DataCollectors><DataCollector friendlyName="Code Coverage" codebase="forge.dll" /></DataCollectors></DataCollectionRunSettings></RunSettings>""");

        Assert.Equal([(RunSettingsExtensions.DataCollector, "Code Coverage forge.dll")], RunSettingsExtensions.Read(path));
    }

    [Fact]
    public void RunSettingsWithOnlyVariablesListNothing()
    {
        string path = Write("<RunSettings><RunConfiguration><EnvironmentVariables><TZ>UTC</TZ></EnvironmentVariables></RunConfiguration></RunSettings>");

        Assert.Empty(RunSettingsExtensions.Read(path));
    }

    [Fact]
    public void AFileThatIsNotRunSettingsXmlIsListedAsUnreadable()
    {
        string path = Write("<RunSettings><DataCollectionRunSettings>");

        Assert.Equal(RunSettingsExtensions.Unreadable, Assert.Single(RunSettingsExtensions.Read(path)).Kind);
    }

    public void Dispose() => TempDirectories.DeleteRecursively(directory);

    private string Write(string content)
    {
        string path = Path.Combine(directory.FullName, "test.runsettings");
        File.WriteAllText(path, content);
        return path;
    }
}
