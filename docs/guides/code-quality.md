# Code quality

`lockflight init` can add a recommended code-quality setup on top of the gates. It is offered, never forced: nothing on this
page happens unless you pass `--quality` or `--architecture-tests`.

**On this page:** [What `--quality` adds](#what---quality-adds) · [Written or proposed](#written-or-proposed) ·
[Turning it on without blocking existing code](#turning-it-on-without-blocking-existing-code) ·
[Architecture tests](#architecture-tests) · [Deterministic or judgement](#deterministic-or-judgement) ·
[Rules audit](#rules-audit)

## What `--quality` adds

| File | What it adds | Protected path |
|---|---|---|
| `Directory.Build.props` | `<AnalysisLevel>latest-recommended</AnalysisLevel>`: the recommended set of the .NET analyzers that ship with the SDK, at the newest level; and `CodeMetricsConfig.txt` as `AdditionalFiles`, so the metrics rules read it | yes |
| `.editorconfig` | a `[*.cs]` section that makes the code metrics rules build warnings: CA1502 (cyclomatic complexity), CA1505 (maintainability index), CA1506 (class coupling). These rules are off by default, also at `latest-recommended` | yes |
| `CodeMetricsConfig.txt` | the thresholds: a method may have at most 15 independent paths (`CA1502(Method): 15`; the rule's own default, 25, lets very branchy methods through), a maintainability index under 10 is flagged (`CA1505: 10`, the default), and class coupling keeps the rule's defaults (`CA1506(Method): 40`, `CA1506(Type): 95`) | yes, a built-in protected path |

Every addition carries the marker `Lockflight quality pack`. A file that already has its part (the marker, or for
`Directory.Build.props` an `AnalysisLevel` and a `CodeMetricsConfig.txt` reference) is left alone, so running `init --quality`
again changes nothing.

## Written or proposed

All three files are protected paths: the agent must not loosen the rules the build gate enforces. `init` treats them as it
treats every protected file:

- **A missing file is written.** `init` writes protected files it starts (the manifest, the CI workflow) and records each one
  in the ledger.
- **An existing file is never changed.** It is the team's own content, so `init` proposes the change instead, through the
  same mechanism as [`lockflight propose`](guardrails-and-boundaries.md#proposing-a-protected-change). It writes a patch to
  `.lockflight/local/proposals/` and prints `git apply --check <patch> && git apply <patch>`. A person reads the patch and
  applies it. The `Directory.Build.props` patch is marked high risk, as every MSBuild change is. Running `init --quality`
  again before the patch is applied writes a fresh patch.

The same holds for a `Directory.Packages.props` that pins package versions centrally: `--architecture-tests` proposes the test
packages' versions rather than adding them.

## Turning it on without blocking existing code

The recommended analyzers and the metrics rules usually find warnings in existing code, and the build gate fails on every
warning the build baseline does not list. Accept today's warnings first:

1. Apply the patches `init` printed, if any.
2. Accept the current warnings: type `/lockflight:baseline build` in Claude Code, or, in a separate terminal window, run
   `.lockflight/bin/lockflight baseline build` from the project root. Recording a baseline is a human decision; the agent
   cannot run it.
3. **Merge the baseline in its own pull request first**, then the pull request with the quality files. CI takes
   `.lockflight/baselines/` from the base branch ([CI hardening](ci-hardening.md)), so a baseline committed together with the
   new rules is ignored when that pull request is judged, and the new warnings would fail it.

New code is then held to the rules, and the accepted warnings are debt you can pay down. A later `baseline build` accepts
whatever warnings exist at that time, so record one only on purpose.

## Architecture tests

`init --architecture-tests` writes a test project that checks the layering Domain → Application → Infrastructure → Api: each
layer may depend only on the layers inside it. It is written only when asked, and only once: an existing project is not
touched.

- **Layers:** the projects whose names end in `.Domain`, `.Application`, `.Infrastructure` and `.Api` (or `.WebApi`); test
  projects are not layers. With fewer than two layers `init` refuses before writing anything. Two projects of one layer (two
  bounded contexts) form one layer.
- **Files:** `tests/<prefix>.ArchitectureTests/` (or under `test/` when only that exists): the project and
  `LayerDependencyTests.cs`, with one test per layer that has outer layers.
- **Library:** [ArchUnitNET](https://github.com/TNG/ArchUnitNET) (Apache-2.0, actively released). NetArchTest is MIT too, but
  `NetArchTest.Rules` has had no release since 2021.
- **Test platform:** xUnit v3 on VSTest, or on Microsoft.Testing.Platform when `global.json` selects it, as the repository's
  other test projects run.

Then:

1. Add the project to your solution (`dotnet sln add <project>`), so the gates build and run it.
2. Add its directory to `protected:` in `lockflight.yaml` (for example `tests/Shop.ArchitectureTests/**`), so the agent cannot
   loosen a rule, and route it to a reviewer in CI ([CODEOWNERS](ci-hardening.md#what-you-add-codeowners-for-the-protected-paths)).
3. A rule the code already breaks fails the test gate: fix the dependency, or accept the failing test with
   `baseline tests`, in its own pull request as above.

The test is a starting point: edit the layers to match the solution, or add rules of your own in the same style, for example
`Types().That().Are(Domain).Should().NotDependOnAny(Types().That().ResideInNamespace("Microsoft.EntityFrameworkCore"))`.

## Deterministic or judgement

| Check | Kind | Label | Why |
|---|---|---|---|
| Analyzer warnings (the recommended set, CA1502, CA1505, CA1506) | deterministic | **G** in a session (Stop gate, `gate run`) and in CI | The build gate fails a warning the build baseline does not list, and a required CI check blocks the merge. It is still not a boundary: a suppression in the same change (`#pragma warning disable`, `[SuppressMessage]`, `<NoWarn>` in a project file) silences the warning. The pr profile's `quality.suppressions` step lists such a suppression when a change adds it (below) |
| The analyzer config itself (`.editorconfig`, `CodeMetricsConfig.txt`, `Directory.Build.props`) | deterministic | **G**: the guard; in CI, `.editorconfig` and `CodeMetricsConfig.txt` come from the base branch | `Directory.Build.props` is not restored from the base branch in CI: a pull request that edits it is judged with its own version, so it needs CODEOWNERS ([CI hardening](ci-hardening.md)) |
| Architecture tests | deterministic | **G**; **B** in CI only with a required check and the test project and the solution file routed to a reviewer | The test gate runs them like any test. The agent can edit the test code unless you protect it, and a pull request can drop the project from the solution |
| New suppressions (`#pragma warning disable`, `[SuppressMessage]`, `<NoWarn>`, `<WarningsNotAsErrors>`, a lowered `.editorconfig` severity, `#nullable disable`, `[ExcludeFromCodeCoverage]`) | deterministic | **G** with `gates.quality.suppressions: fail` (`gate run --profile pr`); **B** in CI only with a required check, since the manifest and the suppressions baseline then come from the base branch | A line scan of what the change adds, not a compiler: see [new suppressions](configuration.md#new-suppressions-fail-the-pr-profile) for what it reads and what it cannot see. In `report` mode (the default) it lists, it does not block |
| The thresholds and the layer map | judgement | a person | They are a decision about your code base, made when the files are applied and reviewed |
| Design quality beyond the rules (naming, cohesion, a good abstraction) | judgement | advisory | The [review of record](commands.md#lifecycle-standard-profile) reports it; only a blocking finding with a failing reproduction test changes a verdict |

**G** is a guardrail and **B** a boundary, as defined in [guardrails and boundaries](guardrails-and-boundaries.md).

## Rules audit

The crew plugin's `rules-audit` skill ("which of my rules are checked by anything?") reads `lockflight facts --json` and your
instruction files, and labels each written rule by what backs it, by exact matches only. Its answer is advice, not a verdict:

- A rule that names an analyzer id (`CA1502`, `IDE0005`), or the metric a code metrics rule measures ("cyclomatic complexity",
  "maintainability index", "class coupling"), is **related** when the facts show a tracked `.editorconfig` or `.globalconfig`
  setting that id to `warning` or `error`. Otherwise the skill suggests the `dotnet_diagnostic` line.
- A rule that bans a dependency between two named layers (Domain, Application, Infrastructure, Api) is **related** when a test
  file uses ArchUnitNET or NetArchTest. Otherwise the skill suggests `init --architecture-tests`.

Related, never checked: a build warning fails only as a new warning against the baseline, and a test checks its own rules,
not the sentence in your instructions.

## Next

- [Configuration reference](configuration.md#baselines-brownfield-adoption-human-only): baselines
- [CI hardening](ci-hardening.md): what CI takes from the base branch
- [Commands](commands.md#setup): `init --quality` and `--architecture-tests`
