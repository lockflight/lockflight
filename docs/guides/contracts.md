# Public contracts

The formats and names that a person, a script or a CI system reads or writes, and what Lockflight promises about each. Read it
before you build on a Lockflight file or output: a dashboard over `verdict.json`, a script that checks `engine.lock`, a
ruleset that requires a status context, a workflow that calls the action. Anything not on this page is internal and may change
in any release.

**On this page:** [The promise](#the-promise) · [The inventory](#the-inventory) · [Reserved names](#reserved-names) ·
[Internal formats](#internal-formats) · [How it is checked](#how-it-is-checked)

## The promise

Since 0.3.0 every contract on this page changes only by addition:

- **A new optional key or property, a new value where a list says values may be added, a new input or output is an addition.**
  It keeps the format's id (`lockflight.ci-evidence/v1` stays `v1`).
- **A removed or renamed key, a new required key, a narrowed type or a changed meaning is a new id** (`v1` → `v2`) and comes
  with a migration in `lockflight update`. A changed default of an existing key or action input counts as a changed meaning.
- **Files the engine writes are read tolerantly:** a reader ignores properties it does not know. An enum value it does not know
  is shown as it is and decides nothing in the reader's favour (an unknown approval state approves nothing).
- **Files a person writes are read strictly:** an unknown key in `lockflight.yaml` or `.lockflight/engine.lock` is an error
  (for the manifest, one that names the engine version reading it). So a manifest that uses a key added in a later release
  needs that release's engine.
- **Committed records stay readable.** A later engine reads the approval records, engine locks and manifests that any release
  since 0.3.0 wrote.

The published JSON Schemas under [`schemas/`](../../schemas/) mirror the engine's readers and are named after the id
(`lockflight.approval.v2.json` for `lockflight.approval/v2`). Where they disagree, the engine is the authority and the schema
is a bug. Each schema's `$id` is its URL on `main`.

## The inventory

### Files and documents

| Contract | Where it lives | Id | Schema | Described in |
|---|---|---|---|---|
| Manifest | `lockflight.yaml` | `version: 2` | [`lockflight.manifest.v2.json`](../../schemas/lockflight.manifest.v2.json), copied to [`lockflight.schema.json`](../../lockflight.schema.json) for editors | [configuration](configuration.md) |
| Engine lock | `.lockflight/engine.lock` | `lockflight.engine-lock/v1` | [`lockflight.engine-lock.v1.json`](../../schemas/lockflight.engine-lock.v1.json) | [the engine lock](configuration.md#the-engine-lock) |
| Release hashes | `engine-hashes.json` in the `Lockflight.Cli` package and each GitHub Release | `lockflight.engine-hashes/v1` | [`lockflight.engine-hashes.v1.json`](../../schemas/lockflight.engine-hashes.v1.json) | [commands](commands.md) (`engine verify-lock`) |
| Approval record | `.lockflight/features/<feature>/approvals/<phase>.json` | `lockflight.approval/v2` (v1 records still read) | [`lockflight.approval.v2.json`](../../schemas/lockflight.approval.v2.json) | [the lifecycle](../architecture.md#the-lifecycle-standard-profile) |
| Gate result | `artifacts/lockflight/result.json`, and the `gates` property of the CI evidence | `lockflight.result/v1` | [`lockflight.result.v1.json`](../../schemas/lockflight.result.v1.json) | [commands](commands.md#gates) |
| CI evidence and verdict | the judge's `evidence.json`, the report's `verdict.json` | `lockflight.ci-evidence/v1` | [`lockflight.ci-evidence.v1.json`](../../schemas/lockflight.ci-evidence.v1.json) | [commands](commands.md#ci) (`ci`, `ci report`) |
| CI review result | the review job's `review-result.json` | `lockflight.ci-review/v1` | [`lockflight.ci-review.v1.json`](../../schemas/lockflight.ci-review.v1.json) | [commands](commands.md#ci) (`ci review`) |
| Blocked report | the `blocked` property of `verdict.json` | `lockflight.blocked/v1` | [`lockflight.blocked.v1.json`](../../schemas/lockflight.blocked.v1.json) | [reasons](reasons.md) |

In the CI evidence, the `verdict` values (`PASS`, `FAIL`, `NOT-EARNED`, `ERROR`) and the `judge` values of the short form
(`missing`, `refused`) are closed lists: no value is added to them within a major version. Reason codes, check ids and
change-class signals may gain values; a reader that meets one it does not know shows it as it is.

### Names and values

| Contract | The public part | Defined in |
|---|---|---|
| Commit trailer | `Lockflight-Red: <cycle>` on a red commit: a real git trailer, the cycle a positive whole number. The trailer says where to look; the judge never trusts it. | [the CI judge](../architecture.md#the-ci-judge), [fix a bug](fix-a-bug.md) |
| Exit codes | `0` PASS, `1` FAIL, `2` blocked, `3` ERROR, `4` configuration, `5` NOT-EARNED | [commands](commands.md#exit-codes) |
| Status contexts | `gates` (the `pr` gates and green at head) and `lockflight/judge` (the rest of the verdict), by name and meaning. Which job, workflow, check type or GitHub app sets them is not part of the contract, nor whether they are set on a pull request's head or on a merge queue's commit. | [CI hardening](ci-hardening.md#the-ci-judge-action) |
| CI action | [`action/action.yml`](../../action/action.yml): its `mode` values (`judge`, `review`, `report`), every input with its default, and the outputs `verdict` and `policy`. Inputs, outputs and modes may be added; an existing input never becomes required. | [CI hardening](ci-hardening.md#the-ci-judge-action) |
| Generated workflows | The workflow `init` writes for a repository: the `judge` job's outputs `verdict` and `policy`. A unit repository of a workspace shell runs the same workflow with the action's `shell-clone` input. Other job ids and step names are not part of the contract. The unit workflow of `workspace sync` (a `gates` job that uploaded `lockflight-result`) was on this list until the command was removed after its deprecation in 0.3.1; a copy already committed keeps running only with the engine it pins, and `update --project` replaces it ([moving a vendored unit](ci-hardening.md#moving-a-vendored-unit-to-the-pointer)). | [CI hardening](ci-hardening.md#workspace-shells-unit-repositories) |
| Artifact names | `lockflight-evidence` (the judge), `lockflight-review` (the review), `lockflight-verdict` (the report), and `lockflight-result` (the gate result). A name says what an artifact should hold, not who wrote it: do not take an artifact's name alone as proof of which job uploaded it. | [CI hardening](ci-hardening.md#the-ci-judge-action) |
| Comment markers | `<!-- lockflight:ci:v1 head=<sha> -->`, the first line of the pull request comment the report finds its own comment by; `<!-- lockflight:begin -->` and `<!-- lockflight:end -->` around the block `init` writes into `CLAUDE.md`; `<!-- lockflight: drift-ignore -->` on a line `docs check` skips | [commands](commands.md) (`ci report`, `docs check`), [troubleshooting](troubleshooting.md) |

`ci.timeoutMinutes` in the manifest is the judge's whole time budget, for everything it runs, not the timeout of one job.
Lockflight does not need Docker to judge a pull request; a team's own tests may. The environment the judge runs the tests in
(network, services, Docker, the file system layout) is described in [CI hardening](ci-hardening.md), not promised.

### The action and the engine

The action installs the engine the target branch pins (`.lockflight/engine.lock`, exactly that version) and calls it. What it
calls is part of the contract, so an action and an engine from different releases keep working together as far as both only
added:

- the commands and options `ci --platform github --event --head --evidence [--base] [--branch] [--no-review-expected]
  [--shell]`, `ci review --platform github --head --base --out [--branch]`, `ci report --evidence --review --head --context
  --out [--base --event --platform --branch]`, `engine verify-lock <file> --base <file> --script <file> [--attestation]` and
  `workspace clone --into <dir>`;
- the exit codes above, and the first line `engine verify-lock` prints on a failure (`engine lock: FAIL <check>: …`);
- the file names `evidence.json`, `gates-result.json` and `refused.json` (the judge's folder), `review-result.json` (the
  review's), and `verdict.json`, `comment.md` and `summary.md` (the report's).

The action does not check which engine version it was given. The workflow `init` writes pins the action of the engine's own
release, and that pairing is the one tested; an action with an engine of another release works only through the additions-only
rule above.

## Reserved names

These names are taken for later additions, so nothing else should use them:

- the workflow job ids `judge-*` and `lockflight-*`, and the artifact name prefix `lockflight-`;
- the CI evidence property `isolation` and the manifest key `ci.isolation`, reserved for future use;
- the reason code `ISOLATION_INCOMPATIBLE`, reserved for future use;
- the commit trailer `Lockflight-Amend`.

## Internal formats

These are the engine's own and may change in any release, without a new id or a deprecation period:

| Format | What it is |
|---|---|
| `lockflight.facts/v1` | the output of `lockflight facts --json`, read by the crew's skills ([the facts document](commands.md#the-facts-document)) |
| `lockflight.ledger/v1` | the lines of `.lockflight/ledger/events-<month>.jsonl` ([the ledger](../architecture.md#the-ledger)) |
| `lockflight.hook-request/v1` | the client-neutral hook input behind `LOCKFLIGHT_HOOK_ADAPTER=neutral` ([the engine on every hook call](../architecture.md#the-engine-on-every-hook-call)) |
| everything else under `.lockflight/` | red/green records, reviews, dismissals, baselines, `class.json`, caches and `.lockflight/local/` |
| text output | the wording of messages, the job summary and the pull request comment below its marker line |
| honesty labels | which control the guides call a guardrail and which a boundary; a label may differ per operating system and only gets stronger |

## How it is checked

Each release's own artefacts are kept under [`schemas/fixtures/released/`](../../schemas/fixtures/released/), one folder per
version, written by that release's published engine with
[`generate.sh`](../../schemas/fixtures/released/generate.sh): it runs `init` on the version's
[`samples/dotnet-single`](../../samples/dotnet-single/), approves a spec in a scratch copy, commits a red/green cycle with
its trailer, judges the branch with `ci --platform github` and `ci report`, and keeps the results with the release's
`engine-hashes.json`, schemas, `action.yml` and exit code table. The review result and the blocked report, which that run does
not write, come from the release's own schema fixtures.

The engine's unit tests (`Schemas/ReleasedContractTests.cs`, `Schemas/ReleasedInterfaceTests.cs`) then require, for every
released version:

- the current engine loads its manifest, engine lock, release hashes, approval record (still valid for its spec), review
  results and blocked reports, and each passes the current schema;
- `ci report` joined from its CI evidence gives the same verdict, the same status contexts and, per check, the same verdict,
  level and reason code; its comment marker and status context names are the current ones; git still reads its red commit's
  trailer as cycle 1; and `init` still finds its `CLAUDE.md` block;
- every JSON Schema it published is still published and only grew: no property removed, none newly required, no type or enum
  value dropped, no `$ref` or `const` changed, no object closed to unknown properties;
- every action input it had is still there with the same default and not newly required, every output is still there, and its
  generated workflow passes only inputs the action has;
- every exit code it listed is still the engine's, with the same meaning.

## Next

- [Configuration](configuration.md): the manifest and the engine lock in full
- [CI hardening](ci-hardening.md): the workflow, the action and the status contexts
- [Commands](commands.md): every command and its exit codes
