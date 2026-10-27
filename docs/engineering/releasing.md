# Releasing

A release publishes the `Lockflight.Cli` tool packages to nuget.org and drafts a GitHub Release, all from
[`.github/workflows/release-packages.yml`](../../.github/workflows/release-packages.yml). Nothing is published from a laptop.

## What the workflow does

1. **Checks the version.** The workflow runs only from a tag `vX.Y.Z[-pre]`. The version is the tag without the `v`, must be
   SemVer 2.0 without build metadata, and must equal the `version` of every `plugins/*/.claude-plugin/plugin.json`.
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

## Cutting a release

1. Raise the version in both `plugin.json` files (and `plugins/lockflight/min-engine-version` when the plugin needs the new
   engine), and draft the CHANGELOG entries in a release pull request. Merge it.
2. Tag the merge commit and push the tag: `git tag -s vX.Y.Z -m vX.Y.Z && git push origin vX.Y.Z`.
3. Approve the `publish` job when the build and smoke tests are green.
4. Edit and publish the draft GitHub Release.

Check a published package against its attestation with
`gh attestation verify Lockflight.Cli.X.Y.Z.nupkg --repo lockflight/lockflight`, using the file from the GitHub Release
(nuget.org adds its repository signature to the copy it serves, so that file's hash differs).
