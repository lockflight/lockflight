namespace Desk.Client.Tests;

/// <summary>
/// A window needs a single-threaded apartment (STA) thread: [StaFact] (Xunit.StaFact) runs the test on one. The first such test
/// in a run takes a few seconds while WPF starts.
/// </summary>
public class MainWindowTests
{
    [StaFact]
    public void TheWindowBindsTheViewModel()
    {
        MainWindow window = new();

        Assert.Same(window.ViewModel, window.DataContext);
    }
}
