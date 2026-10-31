# Changelog

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/). The plugin and the `Lockflight.Cli` engine share one version.

## [Unreleased]

## [0.3.0] — 2026-10-31

0.3.0 makes a CI check the judge of record and the plugin the local assistant. The CI judge re-runs each red commit,
checks green at head, traces the spec, runs the pull-request gates, compares the policy with the base's, and reports the
`gates` and `lockflight/judge` statuses. Its red/green, tests-since-red and trace results are a strong guardrail, not a boundary: the
judge runs the pull request's code in the same job (judge isolation comes after 0.3.0). The manifest, evidence,
`engine.lock` and CI comment formats are frozen from this release: until 1.0 they take additions only.

### Breaking changes
- **`adopt`, backups and undo are removed.** `lockflight init` adopts an existing Claude Code and Spec Kit setup itself
  (`--dry-run`, `--unit-of`); git is the undo (#153).
- **SSH-signed approvals are removed:** `approve spec|tasks --sign [--key]`, `/lockflight:approve … --sign`, the `.sig`
  files and `approve verify --allowed-signers`. The approval of record is the platform's review (CODEOWNERS on `specs/**`).
  A `.sig` file an 0.2 engine wrote is ignored (#138).
- **`rules audit` and `ledger retro` leave the engine** and become the crew's `rules-audit` and `retro` skills, which read
  `lockflight facts --json` (`lockflight.facts/v1`) (#142).
- **Manifest version 2** (`routing:`, `approvals:`, the reserved `autonomy:`, `shell:`, gate toggles, `requiresEngine`).
  `init` writes version 2; `update` moves a version 1 `lockflight.yaml` once, editing its text so comments and order stay
  (#144, #161).
- **Evidence formats:** approval records `lockflight.approval/v2` (`state: person`; a v1 record still counts), the blocked
  report `lockflight.blocked/v1`, gate results v1 (#150); the CI judge's evidence, verdict and review result in the frozen
  formats, with a published schema for each in `schemas/` (#186, #210).
- **The engine is pinned by hash** in `.lockflight/engine.lock` (`lockflight.engine-lock/v1`), with the install script
  vendored beside it (`.lockflight/ci/install.sh`); the `dotnet-tools.json` pin stays as a mirror of the lock's version.
  The CI pipelines install the engine the target branch's lock pins, and CI checks a changed lock against its release
  (#163, #169, #171, #198). A development build keeps its lock in the git-ignored `.lockflight/local/engine.lock`.
- **The guard parses only common shell writes** and allows a command whose writes it does not parse; test-file hashes, the
  review and the CI gates catch what it misses (#137).
- **The plugin needs engine 0.3.0** (`min-engine-version`): it runs `ci`, `route`, `facts`, `engine install` and
  `link --installed`.

### What to do after upgrading
- **Update the engine and the project, in this order.** In a separate terminal window (not with `!` in Claude Code), from
  the project root, run `~/.lockflight/bin/lockflight update` and commit what it lists, in one commit. It installs engine
  0.3.0, moves `lockflight.yaml` to version 2 (listing each new check and how to make it fail), writes
  `.lockflight/engine.lock` and `.lockflight/ci/install.sh`, and moves the CI file's engine install to the lock. Until
  then, Claude Code says at session start that the plugin needs a newer engine. Without the .NET SDK, install the engine
  with `scripts/install.sh --version 0.3.0` (or `install.ps1 -Version 0.3.0`), which prints the engine's path
  (`~/.lockflight/tools/0.3.0/lockflight`), and run that engine's `update --project` from the project root instead.
- **Run `lockflight init` again**, with your level's flag (`--native`, `--ci-only`) if you use one, for the new deny rules
  and sandbox write denials for `.lockflight/engine.lock` and `.lockflight/ci/**`, and an `Edit` deny rule for your user
  install `~/.lockflight/**`; `doctor` names what is missing until then.
- **Add the CI judge.** `init` never replaces a CI file that exists, and `update` only moves its engine install, so an
  0.2 `lockflight-gates.yml` keeps running the gates alone. To judge, delete it and run `lockflight init` (or
  `init --ci-only` for CI without the plugin), which writes the judge's workflow on GitHub or pipeline on Bitbucket (the
  Azure and GitLab jobs run the gates only). Merge it with a person's review, and make `lockflight/judge` a required status check once the team produces
  red commits (a commit with a `Lockflight-Red: <cycle>` trailer; `gates` keeps its name and meaning). Route
  `.github/workflows/**`, `.github/actions/**`, `lockflight.yaml`, `.lockflight/**` and `specs/**` to a code owner;
  `doctor` names what CODEOWNERS leaves uncovered.
- **Approvals move to the platform.** Delete any `.sig` files and `allowed_signers` setup; put `specs/**` under CODEOWNERS
  with required code-owner review. `approve verify` still checks the records and hashes.
- **If you used `adopt`**: nothing to undo; run `init` for new setups. Old backup refs (`refs/lockflight/backup/*`) and
  tarballs are no longer read; delete them when you no longer need them.
- **If you used `rules audit` or `ledger retro`**: install the `lockflight-crew` plugin and use its `rules-audit` and
  `retro` skills.
- **Pick an install level** ([install levels](docs/guides/install-levels.md)): the plugin (default), `init --native`
  (project hooks, no plugin) or `init --ci-only`. CI runs at every level.
- **Update the crew plugin too** (0.3.0 adds the spec writer and the planner); it moves with this release.
- **Keep a cycle test's name until the pull request merges.** The judge reports a cycle test renamed or deleted after its
  red commit as NOT-EARNED, and an amended test whose red version no longer passes on the head's code as NOT-EARNED until a
  new red commit for that cycle proves the new expectation.

### Added
- **The CI judge** (`lockflight ci`, `ci report`, `ci review`, and the composite action `lockflight/lockflight/action`):
  re-runs each red commit (#158) and checks green at head (#159); checks that cycle tests are the ones that went red, with a
  per-method fingerprint of each listed test (#160, #162); re-checks existing tests a pull request changed (advisory,
  #165); lists test-assembly code that could tamper with the test host (#175); traces the spec, runs the gates, keeps a
  time budget and runs the review in its own job (#180); checks approvals and judges a unit with its workspace shell's spec
  (#183); reads red commits and the change class from git (#145, #185); shows Levi's verdict badges in the comment and job
  summary (#187).
- **The `policy` check** ("Policy vs base"): a pull request that loosens `lockflight.yaml` against the target branch's
  (gate modes, protected or critical paths, routing thresholds, approvals, the judge's limits, the `shell:` pointer and
  more) FAILs, and the comment names each loosened setting (#209).
- **The report re-plans the run from git** and refuses evidence (ERROR) whose policy commit, engine, event, class, red
  commits or check levels disagree with the plan, or whose shape is not the schema's; `lockflight.ci-review/v1` is
  published (#210).
- **Generated CI for four platforms:** the GitHub workflow and the Bitbucket pipeline run the judge through the action
  (#182); Azure Pipelines and GitLab CI/CD jobs run the gates (#119), with the policy from the target branch's tip (#199).
- **Risk routing** (guardrails): a pull request's change class from size, project, test, policy and structure signals
  (#147, #156); API surface and database signals (#164); the analyzer's public API files (#174); raising the class from a
  spec's class line, the recorded class (`route record`), labels and `--raise` (#170); what each class requires, the
  comment lines, the evidence block and `route show` (#168); a unit repository's routing is the shell's, made stricter by
  its own (#172).
- **Approvals of record:** CI checks in git what the approval rests on (#166).
- **Three install levels:** `init --native` and `init --ci-only` (#178).
- **Engine install without the .NET SDK**, checked against its hash (`scripts/install.sh`, `install.ps1`,
  `lockflight engine install`, `link --installed`) (#151, #152, #163); the release publishes per-platform executable hashes
  and the install script's hash (#146, #198).
- **Workspaces:** a unit repository's CI judges with its shell's policy through the `shell:` pointer (#167).
- **Red/green and trace for NUnit (#141) and MSTest (#149)**; `test.stopFilter` filters red/green's regression passes
  (#155).
- **Gates:** the stop profile builds and tests only the projects a change affects (#154); `pr` steps for suppressions a
  change adds (#136), references a change breaks in instruction files (#139) and an EF Core model changed without a
  migration (#143).
- **The crew:** a spec writer and a planner on a public spec contract, Spec Kit optional (#131); `crew/v*` releases with a
  trust-boundary check (#176).
- **`init`:** an opt-in code-quality pack and architecture tests (#121).
- **`doctor --self-test`**; the canary suite moves to the engine tests (#140). `doctor` checks CODEOWNERS (#171) and
  recognises the CI-only level (#195).
- **Release:** the marketplace pins each plugin to its own release tag (#179).

### Changed
- **The guard protects your user install** (`~/.lockflight/**`, with an `Edit` deny rule), parses `ln` like `cp`, and judges
  writes into another checkout of a Lockflight project; `link --installed` reads only the checkout's committed lock (#205).
- **A hook precondition failure never blocks Stop, SessionStart or TaskCompleted**; it blocks a tool call and a typed
  human command, and says so to the person. `init` inside a Claude Code session keeps the engine of a project that
  already has a `lockflight.yaml` (#212).
- **An agent waiting on a person's first approval stops cleanly**: the Stop gate is skipped, with one message for the
  person, while the tree and the missing approval are unchanged (#201).
- At the native level, messages and next steps name terminal commands, not `/lockflight:*` (#195).
- `init`'s Spec Kit line names the preset of this engine's plugin version and an engine that exists at the chosen level
  (#211).
- The generated workflows pin the action by commit, labelled with the engine release that wrote them (`# v0.3.0`) (#191).
- Release: each release file is uploaded once, a dry run rehearses the release step, and releases start only from `main`
  (#188); the version files are checked against the release, the crew's included (#190).
- Docs: a README around the CI judge and a QUICKSTART to a first green verdict (#184); the architecture with the CI judge
  as the verdict of record, honest labels and the shell pointer (#181); the judge as it is in 0.3.0 (#189); the feature
  skill commits the feature and its evidence before the review (#126).

### Removed
- `adopt plan|apply|undo`, backup refs and tarballs (#153).
- `approve … --sign [--key]`, `approve verify --allowed-signers` and the `.sig` files (#138).
- `rules audit` and `ledger retro` from the engine (#142).

### Fixed
- The marketplace describes the plugins at its pinned tag (#133).
- Doctor checks the plugin the session loaded, and names the copy (#125); Spec Kit is optional, reported as info (#173).
- `init` adds no adopt step when the preset is already installed (#124).
- The guard lets the agent ask a human-only command for `--help` (#123).
- The active feature is found the way Spec Kit finds it, branch included (#122).
- The secrets step no longer reads the engine's own hashes as API keys (#120).
- `quality.suppressions` no longer fails the `#nullable disable` atop an EF Core migration written without a designer
  (#196).
- The CI judge, from the 0.3.0 review and end-to-end runs: swapped production lines are a change (#192); a pure rename or a
  comment-only edit of production code needs no red (#193); host tampering accepts writes into a fixture's temporary folder
  (#194); a pull request's own `.gitattributes` hides no lines, `NuGet.Config` is restored from the target branch, and
  `ci --shell` routes under the shell's routing (#197); one definition of a shared test-helper project, and test-side files
  added after red are compared (#200); compiled non-C# files, central package removals and unreadable files route as they
  should (#202); an amend after red is accepted only when the red commit's version still passes on the head's code, and a
  weakened one FAILs (#203); a target branch without `lockflight.yaml` is refused, and the red hint names the manual
  trailer (#204); merges of the target branch after a red are no amend (#206); the test fingerprint follows generic bases,
  type arguments and attribute types and ignores the compiler's attributes (#207); a base test the base skips at run time
  with the same message is no new skip (#208).
- The engine lock: every reader agrees on a lock, the first lock is release-checked in the action, and `engine
  verify-lock` checks the install script (`--script`) and names a downgrade (`--base`) (#198). The action's policy restore
  reads `NuGet.Config` too (#213).

## [0.2.0-alpha.9] — 2026-10-28

### What to do after upgrading
- **Update the engine in every project.** The plugin now needs engine 0.2.0-alpha.9 or later: the approve and review skills
  describe what only this engine does (an approval refused by its phase check, the configurable diff limit, the warning for
  a text file git shows as binary, the escalated findings). In a separate terminal window (not with `!` in Claude Code), run
  `lockflight update` from the project root and commit what it lists. Until then, Claude Code tells you at session start
  that the plugin needs a newer engine.
- **An approval now needs a passing phase check.** `lockflight approve spec|tasks` and `/lockflight:approve` run
  `phase check specify` (for the spec) or `phase check tasks` (for the tasks) first and record nothing while it fails; the
  message lists each failure and how to fix it. The tasks can no longer be approved before the spec. A spec written before
  Lockflight often fails because a user story has no Given/When/Then scenario: add the scenarios or rewrite the spec to
  `.specify/templates/spec-template.md`, run the check until it passes, and approve. An adopted feature needs no approval
  while it stays as it was adopted.
- **`.gitattributes` is now a protected path.** The agent can no longer write it; propose a change and make it yourself. Run
  `lockflight init` again to add its `Edit` deny rule to `.claude/settings.json` (`doctor` names it until then), and add it
  to `CODEOWNERS` if you keep one ([CI hardening](docs/guides/ci-hardening.md)).
- **A review whose diff hides a text file is not of record.** When git shows a text file as binary (a `-diff` or `binary`
  attribute, a diff driver), the review still runs, names the file and warns, but it is not the review of record until the
  attribute is gone.

### Added
- **`review.maxDiffCharacters`** in `lockflight.yaml` (default 120,000, from 10,000 up to 400,000). The run records the
  limit, and a run fitted to another limit than the manifest's is not of record. When a diff is cut, the message suggests
  raising the limit (and `review.maxUsd`, since a larger diff costs more).
- **Generated files are listed, not shown, in the review diff:** `packages.lock.json`, `package-lock.json`,
  `pnpm-lock.yaml`, `yarn.lock`, EF Core model snapshots, and migration designers whose migration is in the diff or the tree.
  A snapshot or designer is listed only when its content has EF Core's generated shape; anything else is shown in full. The
  reviewer gets each listed file's path, size and SHA-256 and may read it; listed files do not count toward the limit, and
  the run records them under `generatedFiles`.
- **Under-rated findings that break a MUST or SHALL are escalated.** A minor or nit finding whose quote is an anchored
  requirement sentence with MUST or SHALL, and whose reproduction fails on the current code, is raised to major and then
  treated like any other major finding. The evidence keeps the reviewer's rating (`originalSeverity`) and says why a
  candidate was or was not escalated (`escalation`).
- **The review evidence names the test project each reproduction was built in** (`reproductionProject`), so a reproduction
  that never compiled on a solution with several test projects can be traced.

### Changed
- `approve spec|tasks` and `/lockflight:approve` run the matching phase check before recording, the same way in a terminal
  and in a session; in a session, step 1 refuses before a code is shown.
- The crew's test author ends its report with a `Security note:` line for each instruction it found in the inputs and
  ignored, and the feature skill copies that line verbatim into its reply to you.
- The getting-started guide says what specs written before Lockflight need and how the active feature is found when
  `specs/` holds several.

### Fixed
- **A git attribute can no longer hide a change from the review.** The review diff ignores text conversions, and every
  file git lists as binary is checked by content; a text file shown as binary makes the run not of record. `.gitattributes`
  is protected and is a high-risk file for proposals.
- The commands reference lists every hook the plugin registers, `task-completed` included.

## [0.2.0-alpha.8] — 2026-10-28

### What to do after upgrading
- **Update the engine in every project.** The plugin now needs engine 0.2.0-alpha.8 or later, because the review and
  red/green changes below are in the engine. In a separate terminal window (not with `!` in Claude Code), run
  `lockflight update` from the project root and commit what it lists. It installs the newest engine, re-pins the project to
  it and links it again, which also writes the engine's hash record. Until then, Claude Code tells you at session start
  that the plugin needs a newer engine.
- **A linked engine without a hash record is now refused.** An engine linked before 0.2.0-alpha.7, or copied into
  `.lockflight/bin/` by hand, has no record. The hook shim no longer runs it: reads still pass, but writes and a typed
  `/lockflight:approve` (or baseline, reset, amend) are blocked, and `doctor` fails it without running it. The session start
  message and each refusal say to run `lockflight update` in a separate terminal, which fixes both the version and the record.
- **The review's default model is now `sonnet`**, unless your `lockflight.yaml` sets `review.model`. A project without the
  setting runs its next review of record with `sonnet`; a project that sets it (including `opus`) keeps its model. On our
  review benchmark of seeded defects in a small sample project, `sonnet` found the same defects as `opus`, at about half the
  cost; that is not yet measured on large real codebases. To keep Opus, set `review.model: opus`. Runs already recorded keep
  their model and record status.
- **Commit before you review.** A review over changed, staged or untracked files in the reviewed paths is no longer the
  review of record. It still runs and records its findings, but the output says which files to commit, or to stash with
  `git stash -u`, before reviewing again.
- **A test project whose evaluation fails is NOT-EARNED.** If `dotnet msbuild -getProperty` fails or times out on a
  project, `redgreen start`, `red`, `green`, the amend re-check and `trace` now end NOT-EARNED and name the project, instead
  of leaving it out silently. Fix the project or remove it; for a timeout, raise `gates.timeoutSeconds`. Review runs no
  reproduction while that is so.
- **The linked engine is read-only after a passed check (outside Windows).** Writing into `.lockflight/bin/lockflight` in
  place, for example with `cp -p`, now fails with "Permission denied". Replace it with `lockflight link` or
  `lockflight update`, which put a new file in its place.
- **The plugins are served from release tags.** The marketplace now serves each plugin at the last release tag instead of
  `main`, so `claude plugin update` and auto-update follow releases and a change merged to `main` no longer reaches you
  before its release (and its engine on nuget.org). If you installed from the marketplace before, your copy moves to this
  release with the next `claude plugin update`. To try `main`, use the install from source (`scripts/dev-install.sh`), which
  adds a development marketplace from your checkout.

### Added
- **The plugins are served from the release tag.** Each entry in the marketplace is pinned to the last release tag and its
  commit; the pin moves only after a release is published.
- **The hook shim refuses a linked engine with no hash record**, or an empty one, like a replaced engine. At session start
  it tells you to run `lockflight update`, because the engine that would tell you is not run. `doctor --canaries` plants
  such an engine and checks that `doctor` fails it.
- **`sonnet` is the default review model** (`review.model`); the configuration guide explains the choice and how to pin
  `opus`.

### Changed
- `lockflight:init` asks you to install the engine from nuget.org in a separate terminal
  (`cd ~ && dotnet tool install Lockflight.Cli --prerelease --tool-path ~/.lockflight/bin`) and recommends the `pr` CI
  trigger, since the project then pins a release. An engine built from source still gets the manual trigger.
- The README and the getting-started guide describe the install from nuget.org as the one that works, with the install
  from source for working on Lockflight or trying `main`; the README shows the current package on nuget.org.

### Fixed
- **A passed engine check is reused only for the very file that was checked.** The shim keeps a hard link to the checked
  engine and, outside Windows, makes it read-only, so an engine moved into place with an older modification time, or copied
  over with `cp -p`, is checked again. Where no hard link can be made, every call hashes the engine.
- **A review over uncommitted or untracked changes is not of record.** The reviewer was shown `git diff` of the working
  tree, so a change no commit held could become the review of record, and an untracked file was never shown at all. Files
  git ignores, `.lockflight/`, and untracked `bin/` and `obj/` folders do not count; when git cannot list the changes, the
  run is not of record either.
- **A project whose evaluation fails or times out is NOT-EARNED, not skipped**, in red/green, the amend re-check, `trace`
  and review. A project that evaluates and is not a test project is never reported. `doctor --canaries=full` plants a
  project that fails to evaluate.

## [0.2.0-alpha.7] — 2026-10-28

### What to do after upgrading
- **Update the engine in every project.** The plugin now needs engine 0.2.0-alpha.7 or later, because the in-session
  commands, the amend re-check and the linked engine's hash record are new in it. In a separate terminal window (not with
  `!` in Claude Code), run `lockflight update` from the project root and commit what it lists. It re-pins and re-links the
  engine, which also writes the hash record. Until then, Claude Code tells you at session start that the plugin needs a
  newer engine.
- **`lockflight update` also refreshes the Lockflight block in `CLAUDE.md`**, which now tells the agent never to try a
  human decision and how to propose a protected change. Until you run it, the block is reported as outdated (`adopt plan`
  lists it to be replaced). Commit the new block with the rest.
- **A linked engine without a hash record**, linked before this release, now gets a `doctor` warning, and from 0.2.0-alpha.8
  the hook shim will refuse it. `lockflight update` links the engine again and writes the record.
- **Run `lockflight init` again** in a project set up before this release, to add the new `Edit` deny rules and sandbox
  write denials to `.claude/settings.json`. Your own entries are kept; `--no-sandbox` leaves out the sandbox list. Run it
  again after adding protected files; `doctor` names any it misses.
- **Install from source takes the plugin from your checkout.** `scripts/dev-install.sh` (and the new
  `scripts/dev-install.ps1`) now point the `lockflight` marketplace at the checkout, so the plugin and the engine come from
  the same commit. To go back to the GitHub marketplace: `claude plugin marketplace add lockflight/lockflight`.
- **Scripts that read the status line:** a red Stop gate now reads `stop gate FAIL` (lower case), like `stop gate PASS`.

### Added
- **In-session `/lockflight:baseline`, `/lockflight:reset` and `/lockflight:amend`**, in two steps like
  `/lockflight:approve`: `/lockflight:baseline build|tests|deps [--path <unit>]`, `/lockflight:reset [--feature <id>]` and
  `/lockflight:amend <Class.Method> [--feature <id>] --reason <why>`. Step 1 shows you, not the agent, what would be recorded,
  with a one-time code. Step 2 records it only while that state is unchanged. The steps say `Step 1 of 2` and `Step 2 of 2`,
  the status line shows the line to type while a code waits, and the command typed alone repeats that line. `update` and
  `adopt` stay terminal-only.
- **`lockflight redgreen amend <test> --reason "…"`** (human-only) lets one locked test of the open red cycle change,
  instead of throwing the whole cycle away with `redgreen reset`. Only the file that declares the test becomes writable.
  The next `redgreen red` re-checks the amended tests on a snapshot of the tree as it was at red, so a corrected test must
  still fail even when the implementation already exists. `redgreen green` waits for that re-check. Who amended, when, why
  and the amend's diff are shown in `redgreen status`, `trace` and the pull request summary of `review status`.
- **Test files are checked between red and green.** `redgreen red` records a hash of every file in the locked test
  projects. `redgreen green` and `lockflight review` refuse while one of them was changed, added or deleted without a
  recorded amend, and name each file.
- **`redgreen red` explains each red class** in a line under each test (`--verbose` adds the exception, the deciding stack
  frame and the production member it found), and warns about a new test that contains no assertion.
- **`lockflight docs check`** lists references in `CLAUDE.md`, rules, skills, the Spec Kit constitution and `docs/` that no
  longer resolve: links to missing files or headings, paths that git history deleted or renamed, engine commands or options
  that do not exist, and plugin commands the repository does not have. It also names instruction files over Claude Code's
  size targets. `doctor` shows a short advisory summary of the same scan.
- **`lockflight rules audit`** lists every rule in your instruction files with what backs it: checked by a Lockflight
  guardrail, related to a check, or guidance only, with a suggestion where one fits. It changes nothing.
- **`lockflight doctor --canaries[=full]`**, a self-test: it plants a violation in front of every control in a temporary
  scratch project (never your project) and reports each as `caught`, `MISSED` or `BROKEN`. `=full` also builds a generated
  .NET project and runs the real test gate, red, amend and green on it.
- **A task is completed only after the stop gate passed.** With the stop gate on, the plugin's new `TaskCompleted` hook
  refuses to mark a task in Claude Code's task list completed until the stop gate has passed for the current changes. It
  never builds; `gate run --profile stop` earns the pass in the middle of a turn.
- **Claude Code's own settings back the guard.** `init` and `adopt apply` add an `Edit` deny rule for each protected path
  and list the protected paths as sandbox write denials in `.claude/settings.json`. The sandbox itself is not switched on
  for you: the guide shows how a developer or a team turns it on, and what that costs builds and tests. `doctor` has a new
  `sandbox` check.
- **The linked engine is pinned by its hash.** `link` records the engine's SHA-256 beside it. The hook shim and `doctor`
  refuse an engine replaced after linking, even in the same second as the last passed check: reads still pass, writes and
  in-session commands are blocked, and the message says how to link again.
- **Windows install and update**, tested in CI on windows-latest: `scripts/dev-install.ps1` installs from source on
  Windows, and `lockflight update` there replaces the engine even while it runs. Both installers put `~/.lockflight/bin`
  on `PATH` for new terminals, so `lockflight` works by name (`--no-modify-path` or `-NoModifyPath` skips that).
- **Review findings are checked against the spec and the change's tests.** A blocker or major finding may quote the spec;
  the run records whether the quote appears in it, for you to read (this changes no verdict). When a confirmed finding comes
  with a fix and that fix makes one of the change's own tests fail, the finding is *contested* (NOT-EARNED, naming the test)
  instead of FAIL.

### Changed
- Every hand-off to a person names the place first: the in-session command to type, or a separate terminal window (not
  `!` in Claude Code), with the command inside the sentence rather than on a line of its own.
- The session banner and the `CLAUDE.md` block tell the agent never to run a human decision, even when asked or just to
  try it, and how to change a protected file: write the new version outside the project, run `propose`, and give you the
  `git apply` command.
- `adopt plan` says when `CLAUDE.md` will be created or is empty, instead of "about 0 tokens".
- The Spec Kit preset carries the release version, like the plugins and the engine.
- `workspace sync` says when it rewrites a unit's edited CI workflow (it is generated policy).
- The documentation explains task roles and red classes in the feature skill, the checks a maintainer runs before a
  release, team policy through managed settings and platform notes; questions and ideas go to Discussions.

### Fixed
- **The test gate never passes a run that executed nothing.** A Microsoft.Testing.Platform summary that counts no test that
  ran is NOT-EARNED; a `test.stopFilter` that matches no test is NOT-EARNED and names the filter; when a filter leaves one
  test project empty and others ran tests, the run is judged by the tests that ran.
- **Red/green accepts real assertion failures.** Failures from Shouldly, FluentAssertions, AwesomeAssertions, NSubstitute,
  Moq and Verify, and xUnit v2 assertions over VSTest, now count as RED_ASSERT; before, they were rejected.
- Red/green runs projects on xunit.v3 3.x, shares one time limit across test batches, finds test projects in parallel
  within the time limit, and no longer lists a public class nested in a non-public one as an xUnit v2 test.
- Test runs started by the engine never open a snapshot diff tool on your screen.
- **Review:** reproductions are built in the test project nearest the cited code, with more context for the reviewer and
  one repair attempt for one that does not compile; the reproduction's test method runs, not a helper written before it;
  the reviewer's material is fenced as data, no customization is loaded from the working tree, and project guidance is
  taken from the merge base; two reviews of one project no longer share a build folder; a diff too large to show whole is
  no longer the review of record, which is said before the reviewer starts; a spec quote matches when it leaves out
  Markdown emphasis or code markers.
- **Guard:** it looks through `script`, `xcrun`, `watch` and GNU `parallel`, and blocks writes to the files Claude Code reads
  queued prompts from (scheduled tasks, loops, routines, mailboxes). On Windows, a path with `..` after a link or junction
  is judged the way Windows opens it, which closes a bypass.
- The ledger masks `--password x`, passwords in URLs, credential-named options and variables, and base64 secrets that
  contain `/` or `+`.
- `workspace sync` re-bases every protected glob a unit can match, so a unit's policy is never looser than the shell's.
- `doctor` runs the standard checks for a project that uses the Lockflight Spec Kit preset.
- `lockflight update` moves a project pinned to an older-scheme development build up to a newer prerelease.
- `redgreen reset` and `/lockflight:reset` delete the cycle's red-time snapshots.

## [0.2.0-alpha.6] — 2026-10-27

### Added
- `lockflight-crew`, an optional plugin in the same marketplace, with a test author agent: at the red step of
  `/lockflight:feature` it writes a story's failing tests against the declared stubs until `redgreen red` accepts them. The
  skill checks the recorded red itself and hands over to you after two failed rounds; without the plugin nothing changes.
- A benchmark of the test author against a plain agent on seeded stories (`tests/benchmark/crew/`), including stories
  built to provoke compile reds, test-side exceptions, a missing stub and requirement tagging across several classes.
- Review findings have a lifecycle: each gets a stable id, and a confirmed or unverified blocker/major finding stays open in
  every later `lockflight review` until its reproduction passes (fixed) or you close it with
  `lockflight review dismiss <id> --reason "…"` (human-only, records who, when and why). A later clean run no longer hides it.
- `lockflight review status` lists the review runs and the open, fixed and dismissed findings, with a summary for the pull
  request body (base, target, merge base, number of review runs, open findings).
- A `review:` section in `lockflight.yaml` (`model`, `maxUsd`, `target`). A review is the review of record only with those
  settings against a base whose commit is the merge base of HEAD and the target branch (default: `origin/HEAD`'s branch, else
  `main`); other runs are recorded with the reason and a clean one exits NOT-EARNED. The base now defaults to the target.
- Review evidence records the base and its merge base, the model and budget, the Claude Code version, tokens, the estimated
  cost and the wall time; the ledger gets `review.completed`, `finding.fixed` and `finding.dismissed` (no finding text), which
  the retro counts and the status line shows.

### Changed
- `/lockflight:feature` makes checking the red after the test author a numbered step: the very next action is
  `redgreen status`, quoted in the reply, because the agent's report is not evidence. A missing stub it reports is declared
  by the main session as a stub and the round repeated.
- The documentation has a home page organised by task, an "On this page" list on long guides, "Next" links at the end of
  each, and diagrams of the hook flow, guardrails and boundaries, and the workspace shell sample.
- `lockflight review` uses `opus` by default with a `3.00` budget;
  `--model` and `--budget` still override both.
- The guides now match the engine: the four hooks the plugin installs, every human-only command, red/green on xUnit v2
  as well as v3, what to do while the engine package is not published (install from source; no feed token), and uninstall
  steps for both install modes, the crew plugin and the marketplace.

- While the engine pin is a development build, `init` writes a CI workflow that runs by hand (`--ci pr` still available
  with a warning), and the workflow installs the engine outside `global.json`, so SDK 8 and 9 projects work.
- `doctor` warns when gitleaks is missing or Claude Code is older than 2.1.295; messages name `.lockflight/bin/lockflight`.
- `adopt plan` ends with the exact `adopt apply` command, including the Spec Kit preset path.
- The README and getting started describe the install that works today, the requirements (SDK-style .NET 8, 9, 10; macOS or
  Linux; Claude Code 2.1.295 or later; gitleaks) and a short path for monorepos and existing setups.
- The engine package carries a readme for nuget.org, an icon and third-party notices; the Spec Kit preset is pinned exactly.

### Fixed
- Gates no longer report PASS when they judged nothing or missed a failure: a Microsoft.Testing.Platform run in which every
  test is skipped is NOT-EARNED; a crashed, aborted or timed-out test host is ERROR or FAIL before the known-failing baseline
  is applied; failed test names are read from coloured output in GitHub Actions.
- In a workspace, the Stop gate and `gate run` judge a unit whose state changed since its last PASS, committed or not, with
  committed shared build inputs in each unit's key; `--path`, baselines and a Stop target inside a unit use the unit's own
  `build:`, framework and `stopFilter`. Coverage maps deterministic CI source paths (`/_/…`).
- The shell guard reads nested shell command strings in every spelling (`bash -ce`, `sh -c --`, here-documents, `eval`,
  positional parameters, `while read` loops); a command string it cannot see (`eval "$X"`, `curl … | sh`) is blocked. It
  judges paths where the operating system opens them (`..` after a symbolic link, Unicode composed names, Windows `\\?\`
  and UNC paths), binds `for` loop variables, treats hard links and `rsync --remove-source-files` as writes, and sees more
  writers in POSIX shells and PowerShell.
- A typed `/lockflight:approve` is never passed to the agent when the engine fails or is missing; a waiting code is
  discarded after five wrong codes; the digest describes exactly the bytes the code binds.
- `lockflight review` keeps an open finding's higher severity and re-runs every known reproduction before marking it fixed,
  replays runs in recorded order, and runs the reviewer with explicit permission and sign-in flags.
- `init` and `update` never lower the engine pin; `doctor` and `init` report an unreadable `.claude/settings.json` or tool
  manifest with the fix instead of failing.
- `adopt` installs a Spec Kit version that is not on PyPI from its GitHub tag and keeps a newer Spec Kit than the pinned
  one (installing only the preset); it no longer writes through symbolic links, and `adopt undo` skips unfinished backups.
- `link` copies only a native single-file engine, so platforms without their own package no longer end up with an engine
  that cannot start; the `any` package is gone and macOS Intel (`osx-x64`) has its own.
- The hook shim refuses an engine that is committed to the repository or owned by another user.
- `phase check tasks` no longer counts "Make tests pass…" as a test task; `trace` with no requirements is NOT-EARNED.
- `redgreen red` no longer accepts reds that never reach production code. An assertion counts as RED_ASSERT only when the test
  calls production code (else RED_NO_PRODUCTION); `Assert.Fail`, a throwing fixture or constructor and other test-framework
  failures are RED_IN_TEST; an exception other than the stub's, or one thrown by the runtime or a library, is RED_OTHER and
  is now rejected. Each rejected class is printed with what to change.
- `lockflight review` no longer passes when a blocker/major finding has a reproduction but the tests do not build: the
  finding is unverified (NOT-EARNED).
- The crew test author stops before `redgreen red` when a stub is missing, instead of getting a red of only part of the
  story accepted, which locked the test projects until a person reset them.
- The shell guard treats a drive-relative value (`C:dir`, what an unquoted `C:\dir` becomes in Git Bash) as unknown, so
  writes through it are judged as before.
- `adopt plan` reports hook scripts with the platform's own path separators.
- `init` adds `.claude/worktrees/` to `.gitignore`, so the checkouts Claude Code makes for isolated subagents are not shown
  as untracked files.

## [0.2.0-alpha.5] — 2026-10-15

### Added
- `lockflight update` installs the newest engine from a package feed when Lockflight was not installed from source
  (`--source <feed>` to override); it works whatever SDK a project's `global.json` pins.
- The plugin names the oldest engine it supports; session start and `doctor` report a project engine that is older.

### Changed
- Development builds are numbered after the last release (`0.2.0-alpha.4.dev.<stamp>`), so moving a project to the next
  release is an upgrade; `doctor` suggests `update` for a project still pinned to a development build.

### Fixed
- Text from the project (file names, test names, spec lines, ledger fields) can no longer send escape sequences to your
  terminal; control characters are shown visibly.
- `lockflight propose` refuses names with control characters, files over 8 MB, and a proposals directory that is a symbolic
  link, and handles file names starting with `-`.
- `/lockflight:approve` refuses a tampered or misplaced waiting-code file, a clock that went back, and a `--feature` given as
  a path, and refuses instead of passing the command to the agent when the engine fails.
- The session-start notice about a newer installed engine compared the platform folder name instead of the version.

## [0.2.0-alpha.4] — 2026-10-15

### Added
- Approve a spec or tasks without leaving Claude Code: type `/lockflight:approve spec|tasks`, review what the engine shows
  you, then type the line with its one-time code. It works only when typed in an interactive session, and the approval is
  bound to the content you were shown. The guard blocks any tool call that would queue the command as a prompt (a scheduled
  task, a loop, a message to a session). The ledger's `approval.recorded` line has a `channel` field (`terminal` or
  `session`).
- `lockflight propose <path> --from <file>` (or `--delete`) writes a change to a protected file as a git patch in
  `.lockflight/local/proposals/`, one per file. It prints the protecting rule, a high-risk flag, a diffstat and the
  `git apply` command for you to run, and applies nothing itself. The `CLAUDE.md` block and the guard's block message point
  the agent at it instead of hand-written patches.
- The ledger records what started each gate run (`command` or `stop-hook`), how long it took and whether the Stop cache
  answered it; `ledger retro` shows how many Stop gates ran, how many the cache answered and their total time.
- `doctor` checks that a development tool pin can be restored on this machine (local feed or tools cache).

### Changed
- `lockflight approve --sign` records nothing when signing fails, instead of leaving an unsigned record.
- The skills, the session banner and the `CLAUDE.md` block run the project's linked engine (`.lockflight/bin/lockflight`), so
  commands work when the tool pin is a development build that no package feed has. A stale development pin tells the agent to
  ask you for `lockflight update`.
- A human-only command refused inside Claude Code (also with `!`) says to use a separate terminal window and prints the exact
  line to copy.
- Approvals are recorded in the ledger as `approval.recorded`, with the feature, phase, artifact hash prefixes and signing
  status, and no longer include the approver's email.

### Fixed
- The shell guard follows a variable that the same command line sets to plain text (`S=/tmp/out; cd $S && …`), so writes to a
  scratch directory are no longer blocked. Variables it cannot know exactly are judged as before.
- `git archive -o FILE` is checked as a write to `FILE`.
- Per-unit Stop timings are no longer counted as incidents in `ledger retro` or shown as verdicts in the status line.
- `phase check tasks` names each misplaced task with its line and why it counted as implementation, and says how to mark test
  and stub tasks.
- `phase check tasks` recognises frontend and test-file tasks ("Spec for …", `*.spec.ts`, `*.test.tsx`, `*Tests.cs`, "Cover …
  with tests"), stubs worded "skeleton", "declare signatures" or `NotImplementedException`, and FR ids anywhere on a task line.

## [0.2.0-alpha.3] — 2026-10-15

The first public release. Earlier versions were private previews; this entry summarises what Lockflight does today.

### Added

**Gates**
- `gate run` with the `edit`, `stop` and `pr` profiles: format of the changed lines, build with no new warnings, tests (MTP and
  VSTest, xUnit v3 and v2), patch coverage on changed lines, vulnerable packages, secrets and protected changes.
- Three-valued verdicts (`PASS`, `FAIL`, `NOT-EARNED`) with a `Scope:` line and stable exit codes.
- Baselines for brownfield repositories: accepted build warnings, failing tests and advisories, recorded by a person.
- The Stop gate: build and tests before the agent's turn ends, cached by an inputs hash, naming the failing tests.

**Guard**
- PreToolUse guard for protected paths: edits, and writes from Bash and PowerShell commands, with path canonicalisation
  (symbolic links, Windows short names, alternate streams). Agent worktrees under `.claude/worktrees/` get the same rules.
- Recovery mode on a fresh clone and a tighten-only lockdown.

**Lifecycle (standard profile)**
- A Spec Kit preset: requirement IDs, test-first tasks, `[unit:…]` tags.
- Phase checks, hash-bound approvals (optionally SSH-signed and verified in CI), red/green evidence with a test lock, the
  requirement trace and the review of record with reproduction tests.

**Setup**
- `init` (lite and standard, `--ci pr|manual|none`), `doctor`, `link`, `adopt plan|apply|undo` for an existing Claude Code and
  Spec Kit setup, and `workspace sync` for workspace shells with service repositories beneath them.
- Install from source with `scripts/dev-install.sh`; `lockflight update` brings an install and the current project up to date.
- A generated CI workflow that runs the pinned engine with the base branch's policy.

**Everything else**
- A local ledger of blocks and verdicts, with `ledger retro` for recurring incidents.
- Levi, the pixel lock-keeper, in your terminal and, if you like, in Claude Code's status line (`lockflight statusline`). Only
  people see him.

[Unreleased]: https://github.com/lockflight/lockflight/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/lockflight/lockflight/compare/v0.2.0-alpha.9...v0.3.0
[0.2.0-alpha.9]: https://github.com/lockflight/lockflight/compare/v0.2.0-alpha.8...v0.2.0-alpha.9
[0.2.0-alpha.8]: https://github.com/lockflight/lockflight/compare/v0.2.0-alpha.7...v0.2.0-alpha.8
[0.2.0-alpha.7]: https://github.com/lockflight/lockflight/compare/70afe89...v0.2.0-alpha.7
[0.2.0-alpha.6]: https://github.com/lockflight/lockflight/compare/v0.2.0-alpha.5...70afe89
[0.2.0-alpha.5]: https://github.com/lockflight/lockflight/compare/v0.2.0-alpha.4...v0.2.0-alpha.5
[0.2.0-alpha.4]: https://github.com/lockflight/lockflight/compare/v0.2.0-alpha.3...v0.2.0-alpha.4
[0.2.0-alpha.3]: https://github.com/lockflight/lockflight/releases/tag/v0.2.0-alpha.3
