# Releasing

A release publishes the `Lockflight.Cli` tool packages to nuget.org and drafts a GitHub Release, all from
[`.github/workflows/release-packages.yml`](../../.github/workflows/release-packages.yml). Nothing is published from a laptop.

## What the workflow does

1. **Checks the version.** The workflow runs only from a tag `vX.Y.Z[-pre]`. The version is the tag without the `v`, must be
   SemVer 2.0 without build metadata, and must equal the `version` of every `plugins/*/.claude-plugin/plugin.json` and of
   the Spec Kit preset (`plugins/lockflight/speckit-preset/preset.yml`).
2. **Builds and tests every platform** (osx-arm64, osx-x64, linux-x64, linux-arm64, win-x64), each on its own runner: build
   with warnings as errors, `dotnet test`, pack the Native AOT package, install it with `dotnet tool install` from a local
   feed, check that `lockflight version` prints the version, `lockflight link` it into a scratch project, run the linked
   copy, and run the hook compatibility suite (`tests/compat/probe-suite.sh`) on it.
3. **Packs the pointer package** and writes a CycloneDX software bill of materials.
4. **Publishes, after approval.** The `publish` job runs in the `nuget` environment, so it waits for a required reviewer. It
   writes `SHA256SUMS`, records a build provenance attestation for every package, signs in to nuget.org with Trusted
   Publishing (a short-lived key from GitHub's OIDC token, no stored API key) and pushes the RID packages first and the
   pointer last, with `--skip-duplicate` so a re-run after a partial push finishes the job.
5. **Drafts the GitHub Release** for the tag, with notes from git-cliff (the commits since the previous tag) and the
   packages, `SHA256SUMS` and the SBOM attached. It stays a draft until you publish it.

A manual dispatch from the tag (Actions → release-packages → Run workflow → "Use workflow from": the tag) with `publish` off
runs steps 1 to 3 only: a dry run.

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
4. Edit and publish the draft GitHub Release.
5. Once nuget.org lists the packages, move the marketplace to the release in a pull request of its own:
   `scripts/marketplace-ref.sh set vX.Y.Z` writes the tag and its commit into both entries, copies the descriptions from the
   development marketplace at the tag, and runs the check. Merge it; users
   then get the release from `claude plugin update` or auto-update. If the release is abandoned before this step, users never
   see it.

Check a published package against its attestation with
`gh attestation verify Lockflight.Cli.X.Y.Z.nupkg --repo lockflight/lockflight`, using the file from the GitHub Release
(nuget.org adds its repository signature to the copy it serves, so that file's hash differs).
