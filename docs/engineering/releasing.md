# Releasing

A release publishes the `Lockflight.Cli` tool packages to nuget.org and publishes a GitHub Release, all from
[`.github/workflows/release-packages.yml`](../../.github/workflows/release-packages.yml). Nothing is published from a laptop.

## What the workflow does

1. **Checks the version.** The workflow runs only from a tag `vX.Y.Z[-pre]`. The version is the tag without the `v`, must be
   SemVer 2.0 without build metadata, and must equal the `version` of `plugins/lockflight/.claude-plugin/plugin.json` and of
   the Spec Kit preset (`plugins/lockflight/speckit-preset/preset.yml`).
2. **Builds and tests every platform** (osx-arm64, osx-x64, linux-x64, linux-arm64, win-x64), each on its own runner: build
   with warnings as errors, `dotnet test`, pack the Native AOT package, install it with `dotnet tool install` from a local
   feed, check that `lockflight version` prints the version, `lockflight link` it into a scratch project, run the linked
   copy, and run the hook compatibility suite (`tests/compat/probe-suite.sh`) on it. Then it hashes the executable inside
   its package (`tools/any/<rid>/lockflight[.exe]`) and keeps a bare copy of it (`lockflight-<version>-<rid>[.exe]`). The hash
   is of the executable, not of the package: nuget.org adds its repository signature to every package after the push, which
   changes the package's bytes but not the executable's.
3. **Packs the pointer package** with `engine-hashes.json` at its root: `lockflight.engine-hashes/v1`, the version, the
   tagged commit and the SHA-256 of each platform's executable, merged from the platform jobs (exactly five). It also writes a
   CycloneDX software bill of materials.
4. **Verifies the hashes**, on dry runs too: every RID package is extracted again and compared with `engine-hashes.json`,
   the pointer's copy must be byte-identical, and so must the bare executables. The file that ships describes the packages
   that ship.
5. **Publishes, after approval.** The `publish` job runs in the `nuget` environment, so it waits for a required reviewer. It
   writes `SHA256SUMS` of the packages, the bare executables, `engine-hashes.json` and the install scripts
   (`scripts/install.sh`, `scripts/install.ps1`), records a build provenance attestation
   for all of them, signs in to nuget.org with Trusted Publishing (a short-lived key from GitHub's OIDC token, no stored API
   key) and pushes the RID packages first and the pointer last. A RID package that already exists on nuget.org fails the
   push: Native AOT builds are not guaranteed reproducible, so a re-run must not leave older packages beside hashes of new
   bytes (a partial push needs a new version). It then downloads every package back from nuget.org (polling for up to 30
   minutes while nuget.org validates them) and verifies them against `engine-hashes.json` again.
6. **Publishes the GitHub Release** for the tag, with notes from git-cliff (the commits since the previous tag) and the
   packages, the bare executables, `engine-hashes.json`, `install.sh`, `install.ps1`, `SHA256SUMS` and the SBOM attached. It is published at once, not
   drafted, so the hashes are downloadable as soon as the packages are on nuget.org; edit the notes afterwards.

A manual dispatch from the tag (Actions → release-packages → Run workflow → "Use workflow from": the tag) with `publish` off
runs steps 1 to 4 only: a dry run. A dispatch from a branch is a dry run of version `0.0.0-ci.<run number>`; its artifacts
(packages and `engine-hashes.json`) can fill a test feed.

## One-time setup

In GitHub (repository **Settings**):

1. **Environments → New environment** `nuget`.
   - **Required reviewers:** add the maintainers who approve releases; tick **Prevent self-review** when there is more than
     one.
   - **Deployment branches and tags:** "Selected branches and tags", add a **tag** rule `v*`. A run from a branch then cannot
     use the environment, even when its workflow file was changed.
   - **Environment variables:** `NUGET_USER` = the nuget.org account name (the profile name, not the email address) that
     owns the package.
2. **Rules → Rulesets → New tag ruleset** for `v*`: restrict creation, update and deletion to maintainers, so only they can
   start a release.

On nuget.org (signed in as `NUGET_USER`):

1. **Trusted Publishing → Add policy:** repository owner `lockflight`, repository `lockflight`, workflow file
   `release-packages.yml`, environment `nuget`.
2. Optional, recommended: **reserve the ID prefix** `Lockflight.` (request it from the nuget.org support page), so other
   accounts cannot publish `Lockflight.*` packages.

## Checks before a release

Three checks start real Claude Code sessions, which need a Claude login. The repository holds no Claude Code token, so CI
cannot run them; a maintainer runs them on the release candidate on `main` before the release pull request, and notes the
Claude Code version and the results in it:

1. **The plugin evals**, all three suites, as [CONTRIBUTING](../../CONTRIBUTING.md#skill-evals) shows
   (`claude plugin eval plugins/lockflight …`, `plugins/lockflight-crew …` and `plugins --tag both-plugins …`). Read every failing case: a skill
   that regressed blocks the release until it is fixed.
2. **The real-hook canary** on an engine built from the same commit:
   `dotnet publish engine/src/Lockflight.Cli -c Release -r <rid> -o out`, then
   `ENGINE="$PWD/out/lockflight" sh tests/canary/run.sh haiku`, which must print only `ok` lines. The `canary` workflow runs
   the same script by manual dispatch once the repository has a `CLAUDE_CODE_OAUTH_TOKEN` secret.
3. **The prompt-expansion probe.** In a scratch project, load a throwaway plugin with a command of its own and a
   `UserPromptExpansion` hook that logs what it receives, have the agent queue that command with `CronCreate`, and see
   whether the hook fires for the queued prompt. Claude Code's documentation says the event is for typed commands; while it
   still fires for queued ones, the approve relay must stay (see
   [the approval row](../guides/guardrails-and-boundaries.md#every-control-labelled)).

## How the marketplace serves a release

Users add the marketplace from `main`, but [`.claude-plugin/marketplace.json`](../../.claude-plugin/marketplace.json) does not
serve the plugins from `main`: each entry is a `git-subdir` source at the last release tag (`ref`) and that tag's commit
(`sha`). A merge to `main` therefore changes nothing for users; only moving the pin does. Claude Code fetches the plugin at
`sha` and keeps it under its `plugin.json` version, and `claude plugin update` and auto-update replace an installed copy only
when that version changes. So the pin moves only to a new release tag, whose plugins carry the new version.

The pin moves **after** the release is published, in a pull request of its own, never in the release pull request: a ref to a
tag that does not exist yet fails every install and update, and a plugin served before its engine is on nuget.org can ask
(`min-engine-version`) for an engine nobody can install. `scripts/marketplace-ref.sh` (run in CI on every pull request and
push to `main`) fails unless:

- every entry names the same tag and commit, and the tag exists on GitHub and points at that commit;
- at that commit, both `plugin.json` files have the tag's version, and `min-engine-version` is at most that version;
- nuget.org lists `Lockflight.Cli` at the tag's version and at `min-engine-version`;
- the marketplace and plugin descriptions are those of the development marketplace,
  [`plugins/.claude-plugin/marketplace.json`](../../plugins/.claude-plugin/marketplace.json), **at that tag**, not on `main`:
  users get the tag's plugins, so they read what the tag says about them, and `main` may describe plugins not yet released;
- the development marketplace in the checkout serves each plugin from the checkout (`scripts/dev-install.sh` adds it, to try
  `main`).

The release pull request raises the versions on `main` while the pin still names the previous tag, so the check passes
throughout and users stay on the previous release until the last step below.

## Cutting a release

1. Raise the version in both `plugin.json` files and in the Spec Kit preset's `preset.yml` (and
   `plugins/lockflight/min-engine-version` when the plugin needs the new engine), and draft the CHANGELOG entries in a release
   pull request. Leave `.claude-plugin/marketplace.json` alone. Merge it.
2. Tag the merge commit and push the tag: `git tag -s vX.Y.Z -m vX.Y.Z && git push origin vX.Y.Z`.
3. Approve the `publish` job when the build and smoke tests are green.
4. Edit the notes of the GitHub Release the workflow published.
5. Once nuget.org lists the packages, move the marketplace to the release in a pull request of its own:
   `scripts/marketplace-ref.sh set vX.Y.Z` writes the tag and its commit into both entries, copies the descriptions from the
   development marketplace at the tag, and runs the check. Merge it; users
   then get the release from `claude plugin update` or auto-update. If the release is abandoned before this step, users never
   see it.

Check a published package against its attestation with
`gh attestation verify Lockflight.Cli.X.Y.Z.nupkg --repo lockflight/lockflight`, using the file from the GitHub Release
(nuget.org adds its repository signature to the copy it serves, so that file's hash differs). `engine-hashes.json` and the bare
executables are attested the same way; the executable inside nuget.org's copy of a package is byte-identical to the one in
`engine-hashes.json`.

## Releasing the crew

The `lockflight-crew` plugin is released on its own, from the same repository. Its tags are
`crew/vX.Y.Z[-pre]`; the judge's are `vX.Y.Z[-pre]`. A crew tag never starts the judge's workflow, and the changelog's
`tag_pattern` (`^v[0-9].*`) never reads a crew tag as a judge release.
[`.github/workflows/release-crew.yml`](../../.github/workflows/release-crew.yml) runs for a pushed crew tag, and does this:

1. **Checks the version.** It checks that the tag without `crew/v` equals the `version` in
   `plugins/lockflight-crew/.claude-plugin/plugin.json`. It also checks that `plugins/lockflight-crew/min-engine-version`
   names an engine that nuget.org lists: the crew calls the engine (`phase check`, `redgreen red`, `facts --json`), so it is
   never released before the engine it needs.
2. **Checks the trust boundary.** The judge gives facts and verdicts; the crew gives advice.
   [`scripts/crew-trust-boundary.sh`](../../scripts/crew-trust-boundary.sh) fails when the crew ships any of these:
   - hooks: a `hooks/` directory, a `hooks` key in `plugin.json`, or `hooks:` in an agent's or skill's frontmatter;
   - a `bin/` directory or an executable file outside `evals/` (eval scaffolds run in the eval sandbox), MCP or LSP
     servers, settings or output styles;
   - a guard or policy file: a manifest, `.lockflight/`, an engine lock, CODEOWNERS, a schema or the Spec Kit preset;
   - in an agent or skill, a verdict record (approvals, red/green evidence, review records, results, baselines), or a command
     that records a person's decision or the review of record (`approve` other than `approve verify`, `baseline`,
     `redgreen reset`/`amend`, `review` other than `review status`, `update`, `engine install`, global options before the
     verb included). Naming a session command such as `/lockflight:approve spec` for the person to type is allowed. The
     check reads the text; it is a guardrail for the crew's authors, and the engine's own refusals of human-only
     commands still apply.

   The `plugin` job in CI runs the same check on every pull request. Then the workflow validates the plugin with
   `claude plugin validate --strict`.
3. **Packs the plugin.** It writes `lockflight-crew-<version>.tar.gz` from the tagged commit's `plugins/lockflight-crew`
   (with `git archive`, so exactly the committed files), with `SHA256SUMS`, and runs the trust check again on what it
   archived.
4. **Publishes a GitHub Release** named `Lockflight crew <version>`. The release is never marked latest, because the latest
   release is the judge's, which the install script and the docs point at. Its notes come from the commits that touched
   the crew since the previous crew tag. There is no NuGet package and no approval environment: pushing the tag is the
   maintainer's decision.

A dispatch from a branch is a dry run, which checks and packs `0.0.0-ci.<run number>` and publishes nothing. Until the
marketplace pins each plugin to its own tag, the published marketplace still serves the crew from the judge's tag
(see [how the marketplace serves a release](#how-the-marketplace-serves-a-release)).
