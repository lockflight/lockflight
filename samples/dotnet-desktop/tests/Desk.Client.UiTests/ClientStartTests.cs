using System.Diagnostics;

namespace Desk.Client.UiTests;

/// <summary>
/// A smoke test of the real application: start it, wait for its window, stop it. Hosted Windows CI runners have a desktop, so it
/// runs there too. Starting and killing a process is what the CI judge's host-tampering check lists; on the target branch it is
/// information, and a person accepts a new one with a baseline entry (see the desktop guide).
/// </summary>
public class ClientStartTests
{
    private const int StartTimeoutMilliseconds = 30_000;

    [Fact]
    [Trait("Category", "UI")]
    public void TheClientStartsAndShowsItsWindow()
    {
        // WPF projects leave System.IO out of their implicit usings (Path would clash with System.Windows.Shapes.Path).
        string client = System.IO.Path.Combine(AppContext.BaseDirectory, "Desk.Client.exe");
        using Process process = Process.Start(new ProcessStartInfo(client) { UseShellExecute = false })!;
        try
        {
            Assert.True(process.WaitForInputIdle(StartTimeoutMilliseconds));
        }
        finally
        {
            process.Kill();
        }
    }
}
