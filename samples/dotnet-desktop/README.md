# Desktop sample

A WPF client and an ASP.NET Core server in one solution, on .NET 8 (`global.json` pins SDK 8). The
[desktop guide](../../docs/guides/desktop.md) explains how Lockflight works with a solution like this.

| Project | Target | What it shows |
|---|---|---|
| `src/Desk.Client` | `net8.0-windows`, WPF | a window (`MainWindow.xaml` and its code-behind) bound to `ViewModels/OrderViewModel`, where the behaviour lives |
| `src/Desk.Server` | `net8.0`, ASP.NET Core | a minimal API over `PriceList` |
| `tests/Desk.Client.Tests` | `net8.0-windows`, xUnit v2 | ViewModel tests (any thread, milliseconds) and one window test on an STA thread (`[StaFact]` from `Xunit.StaFact`) |
| `tests/Desk.Client.UiTests` | `net8.0-windows`, xUnit v2 | a UI smoke test that starts `Desk.Client.exe`, tagged `Category=UI` |
| `tests/Desk.Server.Tests` | `net8.0`, xUnit v2 | `PriceList` and the API through `WebApplicationFactory<Program>` |

- **UI tests stay out of the cycles.** `tests/e2e/desktop.sh` sets `gates.test.stopFilter: "Category!=UI"`, so the Stop gate
  and red/green's regression passes leave the UI smoke test out. The `pr` profile and the CI judge run it.
- **Windows only for the client.** The client and its test projects build off Windows only with `EnableWindowsTargeting=true`,
  and their tests run only on Windows. The gates handle both off Windows and say what they left out.
- **The first STA test is slow.** It starts WPF, about 5 seconds; ViewModel tests take milliseconds.
- **`System.IO` is not implicit in WPF projects.** It would clash with `System.Windows.Shapes.Path`, so the UI test writes
  `System.IO.Path` in full.
