using Lockflight.Cli.Config;
using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Tests.Routing;

public sealed class XamlBehaviourTests
{
    private const string WindowPath = "src/App/MainWindow.xaml";
    private const string WpfProject = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0-windows</TargetFramework>\n    <UseWPF>true</UseWPF>\n  </PropertyGroup>\n</Project>\n";
    private const string PlainProject = "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n";

    private const string Window =
        "<Window x:Class=\"App.MainWindow\">\n" +
        "  <StackPanel Margin=\"12\">\n" +
        "    <CheckBox IsChecked=\"True\" />\n" +
        "    <Button Content=\"Clear\" Click=\"OnClear\" />\n" +
        "    <TextBlock Text=\"{Binding Total}\" />\n" +
        "  </StackPanel>\n" +
        "  <x:Code><![CDATA[\n" +
        "    void OnClear(object sender, RoutedEventArgs e) { }\n" +
        "  ]]></x:Code>\n" +
        "</Window>\n";

    public static TheoryData<string, string, string?> Edits => new()
    {
        { "a margin", Window.Replace("Margin=\"12\"", "Margin=\"16\"", StringComparison.Ordinal), null },
        { "a checked state", Window.Replace("IsChecked=\"True\"", "IsChecked=\"False\"", StringComparison.Ordinal), null },
        { "a binding path", Window.Replace("{Binding Total}", "{Binding Sum}", StringComparison.Ordinal), null },
        { "a new click handler", Window.Replace("<TextBlock", "<Button Click=\"OnSave\" />\n    <TextBlock", StringComparison.Ordinal), "an event handler (Click)" },
        { "a removed click handler", Window.Replace(" Click=\"OnClear\"", string.Empty, StringComparison.Ordinal), "an event handler (Click)" },
        { "a loaded handler", Window.Replace("<StackPanel Margin=\"12\">", "<StackPanel Margin=\"12\" Loaded=\"OnLoaded\">", StringComparison.Ordinal), "an event handler (Loaded)" },
        { "a selection handler", Window.Replace("<TextBlock", "<ListBox SelectionChanged=\"OnPick\" />\n    <TextBlock", StringComparison.Ordinal), "an event handler (SelectionChanged)" },
        { "a command", Window.Replace("<TextBlock", "<Button Command=\"{Binding Save}\" />\n    <TextBlock", StringComparison.Ordinal), "a command (Command)" },
        { "a converter", Window.Replace("{Binding Total}", "{Binding Total, Converter={StaticResource Money}}", StringComparison.Ordinal), "a converter" },
        { "code inside x:Code", Window.Replace("RoutedEventArgs e) { }", "RoutedEventArgs e) { Close(); }", StringComparison.Ordinal), "an x:Code block" },
    };

    [Theory]
    [MemberData(nameof(Edits))]
    public async Task AXamlEditInAWpfProjectFiresConfigChangedOnlyWhenItWiresBehaviour(string edit, string edited, string? wired)
    {
        Classification classification = await ClassifyAsync(WpfProject, edited, TestContext.Current.CancellationToken);

        ChangeSignal? config = classification.Signals.SingleOrDefault(signal => signal.Id == SignalIds.ConfigChanged);
        if (wired is null)
        {
            Assert.True(config is null, $"{edit}: {config?.Detail}");
            Assert.Equal(ChangeClass.Fix, classification.Class);
        }
        else
        {
            Assert.NotNull(config);
            Assert.Equal($"XAML that wires behaviour changed: {wired}", config.Detail);
            Assert.Equal(WindowPath, config.Where);
        }
    }

    [Fact]
    public async Task AVisualXamlEditOutsideAXamlUiProjectStillFiresConfigChanged()
    {
        Classification classification = await ClassifyAsync(PlainProject, Window.Replace("Margin=\"12\"", "Margin=\"16\"", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        ChangeSignal config = Assert.Single(classification.Signals, signal => signal.Id == SignalIds.ConfigChanged);
        Assert.Equal("production configuration file changed", config.Detail);
    }

    private static async Task<Classification> ClassifyAsync(string project, string edited, CancellationToken cancellationToken)
    {
        Dictionary<string, string> baseFiles = new(StringComparer.Ordinal)
        {
            ["src/App/App.csproj"] = project,
            [WindowPath] = Window,
        };
        using RoutingRepository repository = await RoutingRepository.CreateAsync(baseFiles, cancellationToken);
        repository.Write(WindowPath, edited);
        await repository.CommitAsync("change", cancellationToken);
        ChangeSetReading reading = await ChangeSetReader.ReadAsync(repository.Root, RoutingRepository.BaseBranch, "HEAD", cancellationToken);
        Assert.True(reading.Status == ChangeSetReadStatus.Read, reading.Message);
        RoutingPolicy policy = RoutingPolicy.From(null, new PolicySource(RoutingRepository.BaseBranch, "base", ManifestBlob: null, "default"));
        return ChangeClassifier.Classify(reading.ChangeSet!, policy, []);
    }
}
