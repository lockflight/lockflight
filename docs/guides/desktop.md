# Desktop applications

How Lockflight works with a WPF, Windows Forms or WinUI client, alone or in one solution with an ASP.NET Core server. The
[desktop sample](../../samples/dotnet-desktop/) is such a solution, and [`tests/e2e/desktop.sh`](../../tests/e2e/desktop.sh)
runs the CI judge on it.

**On this page:** [Runner choice and cost](#runner-choice-and-cost) · [Off Windows](#off-windows) ·
[ViewModel tests and UI tests](#viewmodel-tests-and-ui-tests) · [STA attributes](#sta-attributes) ·
[A red in code-behind](#a-red-in-code-behind) · [XAML changes](#xaml-changes) ·
[Host-tampering findings in desktop tests](#host-tampering-findings-in-desktop-tests) ·
[The plugin on Windows](#the-plugin-on-windows)

## Runner choice and cost

A project that targets Windows (`net8.0-windows`, `UseWPF`, `UseWindowsForms`) builds and runs its tests only on Windows. The
CI judge runs every test of the pull request, so a solution with Windows test projects needs its judge job on a Windows runner
(`runs-on: windows-latest`). Hosted Windows runners have a desktop, so window tests and UI smoke tests that start the
application run there too.

Windows runners are slower and dearer:

- **Time:** on the desktop sample, a judged pull request takes about 3 to 4 minutes on `windows-latest` with Defender on. The
  same server-only change takes about 1.5 minutes on `ubuntu-latest`. Most of the difference is the runner, not WPF.
- **Billing:** on a private repository a Windows minute counts twice and every job rounds up to whole minutes, so one judge
  run costs about four times what the Linux run costs. Public repositories are not billed.
- **Extra red cycles are cheap:** each one rebuilds incrementally (about 15 seconds on the sample). The first build at head is
  the expensive one.

A server that has its own solution, with no Windows projects in it, can keep its judge on Linux. So can a solution whose only
Windows project is the app itself, with plain `net8.0` test projects: the judge builds it there with `EnableWindowsTargeting=true`.
Only the projects the judge builds count (`gates.build` or the root's only solution), so a WPF sample elsewhere in the tree does
not move the judge to Windows.

## Off Windows

On macOS or Linux the SDK builds a Windows project only with `EnableWindowsTargeting=true`, and its tests cannot start (the
machine has no `Microsoft.WindowsDesktop.App`). A person's gate runs there (the Stop gate, `gate run`) build with that property,
so client code that does not compile still fails, and leave the test projects that target Windows out. The `test` step says
which ones:

```text
test: PASS (3273 ms)
  total 2; left out off Windows: Desk.Client.Tests, Desk.Client.UiTests (they target Windows and cannot start here; …)
```

`lockflight doctor` explains the same on such a machine. Those tests then run on a Windows machine and on the Windows CI runner.
Red/green for a client test needs Windows too. See
[projects that target Windows, off Windows](configuration.md#projects-that-target-windows-off-windows).

## ViewModel tests and UI tests

Put behaviour in the ViewModel (MVVM). A ViewModel is plain C#, so its tests run fast, on any thread, and a red on it is
an ordinary RED_STUB or RED_ASSERT. Keep three kinds of tests apart:

| Kind | Example | Thread | Speed | Where it runs |
|---|---|---|---|---|
| ViewModel | the total, the discount, a property change | any | milliseconds | every gate, red/green |
| View | create the window, assert a binding or the title | STA | the first one in a run starts WPF (about 5 s) | every gate, red/green, on Windows |
| UI automation | start the application, click, take a screenshot | separate process | seconds | CI, and a person's full run |

Give UI automation tests a category and keep them out of the Stop gate and red/green's regression passes with
`gates.test.stopFilter`. The `pr` profile and the CI judge still run them:

```yaml
gates:
  test:
    stopFilter: "Category!=UI"   # xUnit [Trait("Category", "UI")]; NUnit [Category("UI")]; MSTest [TestCategory("UI")]
```

## STA attributes

A WPF window or control needs a single-threaded apartment (STA) thread. Each framework has its own way, and Lockflight
classifies all of them the same as any other test. The deciding frame of a failure stays the production member, also through
`Dispatcher.Invoke`.

| Framework | Attribute |
|---|---|
| xUnit | `[StaFact]`, `[StaTheory]` from the `Xunit.StaFact` package |
| NUnit | `[Apartment(ApartmentState.STA)]` on the test or the fixture |
| MSTest | `[STATestMethod]` |

WPF projects leave `System.IO` out of their implicit usings, because `Path` would clash with `System.Windows.Shapes.Path`. Test
helpers write `System.IO.Path`, `System.IO.File` and `System.IO.Directory` in full.

## A red in code-behind

Code-behind (`MainWindow.xaml.cs`) is production code. A change to it needs a red like any other, so the stub goes there and an
STA test calls it:

```csharp
public string ItemTitle() => throw new NotImplementedException();   // MainWindow.xaml.cs, in the red commit
```

```csharp
[StaFact]
[Trait("Requirement", "FR-002")]
public void TheTitleShowsTheItemCount()
{
    MainWindow window = new();
    window.ViewModel.Quantity = 3;

    Assert.Equal("Desk (3 items)", window.ItemTitle());
}
```

A one-line tweak in an event handler (focus, select-all) is hard to test that way. That is one more reason to keep handlers
thin and move logic to the ViewModel.

A test that fails while the window's XAML loads (a `XamlParseException`: a missing resource, type or name) fails in the view,
not in the behaviour it is about. Red rejects it as RED_OTHER and says to test the ViewModel instead, or to stub a member the
view calls. A common cause: a window created in a test has no `Application`, so a `{StaticResource …}` defined in `App.xaml` is
not found.

## XAML changes

In a project that sets `UseWPF`, `UseMaui` or `UseWinUI` (directly or in a `Directory.Build.props` above it), a `.xaml` file
has its own [file role](risk-routing.md#file-roles): it counts toward size, but it is neither code nor configuration. A change
to XAML alone needs no red, and a margin, a colour or a binding path stays in the class its size gives it. A changed line that
wires behaviour (an `x:Code` block, an event handler such as `Click="OnClear"`, a `Command=` or a `Converter=`) fires
`config.changed`, which raises the change to `behaviour` and keeps it out of the
[structural lane](spec-contract.md#specmd); move that logic into the ViewModel, where a change needs its red. Green runs the existing tests:
a window test catches a view that no longer loads. A `.xaml` file of any other project, a Workflow Foundation definition
included, is production configuration, and every change to it fires `config.changed`.

## Host-tampering findings in desktop tests

The CI judge's [host-tampering check](ci-hardening.md) lists code in test assemblies that could forge a result. Desktop test
helpers often contain three of its kinds:

| Helper code | Finding | What to do |
|---|---|---|
| `Process.Start` / `ProcessStartInfo` and `Process.Kill` in a UI smoke test that starts the application | starting processes | keep the launcher in one method and accept it with a method-level entry, `<assembly>\|<kind>\|<Type::Method>`, in `.lockflight/baselines/host-tampering.json`, merged on the target branch in its own pull request |
| `AppDomain.CurrentDomain.UnhandledException += …` to log crashes | process-exit and unhandled-exception handler | log through `Application.DispatcherUnhandledException` instead, which is not listed, or accept the method |
| screenshots written under `AppContext.BaseDirectory`, next to the test assembly | file write | write them under a temporary folder (`Directory.CreateTempSubdirectory()`, `Path.GetTempPath()`): a method that visibly asks for one is not listed |

`Application`, `Dispatcher`, `DispatcherFrame` and `Dispatcher.PushFrame` are not listed. A finding the target branch already
has is shown as information. Only a new one makes the check NOT-EARNED, and the judge prints its baseline entry ready to paste.

## The plugin on Windows

To run the WPF tests locally, run Claude Code and the plugin on Windows ([install on Windows](getting-started.md#install-on-windows)):
the hooks run under Git Bash, and the Stop gate runs the client's tests there, STA included. Native Windows has no Claude
Code sandbox, so shell writes to protected paths are checked by the guard only ([guardrails and
boundaries](guardrails-and-boundaries.md)). On macOS or Linux the same project works, with the client's tests left to Windows
as described [above](#off-windows).

## Next

- [Risk routing](risk-routing.md): file roles and classes
- [Configuration](configuration.md): `gates.test.stopFilter` and the gate profiles
- [Reasons](reasons.md): what each rejection means
