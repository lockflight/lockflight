# CI on Azure Pipelines, Bitbucket and GitLab

`lockflight init --ci-platform azure|bitbucket|gitlab` writes a CI job for that service instead of the GitHub workflow. It is
the same job as the [GitHub workflow](ci-hardening.md#what-the-generated-workflow-does), translated: this page says where each
file goes, how to make the job a required check, and what stands in for CODEOWNERS.

**On this page:** [What every template does](#what-every-template-does) · [Azure Pipelines](#azure-pipelines) ·
[Bitbucket Pipelines](#bitbucket-pipelines) · [GitLab CI/CD](#gitlab-cicd) ·
[Paths to route to a reviewer](#paths-to-route-to-a-reviewer) · [Guardrail or boundary](#guardrail-or-boundary)

> [!WARNING]
> **These templates are not yet verified on the live services.** Their shell steps (restoring the policy from the base commit,
> gitleaks, the engine install and the gates) were run in the .NET SDK container against a test repository, under `sh`
> (dash) and under `bash -eo pipefail`. What has not been run is each service's own part: its triggers, its pull request
> variables, its checkout, its artifacts and its merge checks; the Azure steps have not been run at all. Watch the first run, and
> [open an issue](https://github.com/lockflight/lockflight/issues/new/choose) when something does not work.

## What every template does

The same steps as the GitHub workflow, in the same order:

- **Policy from the base commit** on a pull request: the engine lock and the version pin, `lockflight.yaml`, the gitleaks
  policy, the root `nuget.config`, `.lockflight/baselines/`, `.lockflight/ci/` and the code-style and analyzer config anywhere
  in the tree. The list is the GitHub
  workflow's, from the same source in the engine, and a test checks that they match.
- **Pinned tools:** gitleaks at a fixed version, checked against its SHA-256; the engine the base commit pins, installed
  outside the repository, so a `global.json` that pins SDK 8 or 9 does not matter: with `.lockflight/engine.lock`, by the
  vendored install script checked against the lock (no .NET SDK needed for it; the SDK image reads the package with `perl`),
  else by the version in the tool pin with `dotnet tool install --tool-path`. The same step as the GitHub workflow's, from the
  same source in the engine.
- **`lockflight gate run --profile pr --base <base sha>`**, with the result in `artifacts/lockflight/result.json`.

| | Azure Pipelines | Bitbucket Pipelines | GitLab CI/CD |
|---|---|---|---|
| File `init` writes | `.azure-pipelines/lockflight-gates.yml`, a pipeline of its own | `bitbucket-pipelines.yml` | `.gitlab/ci/lockflight-gates.yml`, and a `.gitlab-ci.yml` that includes it |
| When the team already has a pipeline file | untouched: this is a separate pipeline | untouched: the job goes to `.lockflight/ci/bitbucket-pipelines.yml` to merge by hand | `.gitlab-ci.yml` untouched: `init` prints the `include` to add |
| Base on a pull request | merge base of `HEAD` and `origin/<System.PullRequest.TargetBranch>` | merge base of `HEAD` and `origin/$BITBUCKET_PR_DESTINATION_BRANCH` | `$CI_MERGE_REQUEST_DIFF_BASE_SHA` |
| Base on a push to `main` | the previous commit | the previous commit | `$CI_COMMIT_BEFORE_SHA` |
| .NET | `UseDotNet@2` for each channel the test projects need | the `mcr.microsoft.com/dotnet/sdk:10.0` image, older channels added with `dotnet-install.sh` | as Bitbucket |
| `--ci manual` | `trigger: none`, `pr: none` | a `custom` pipeline | a rule for pipelines started from the web |

Where they differ from the GitHub workflow:

- The engine pin and the lock are read with `grep` and `sed`, not Python, because the SDK image has no Python.
- **Boundary, per platform:** every platform runs a pipeline definition the pull request can edit (GitHub from the merge
  ref, Bitbucket and GitLab from the source branch), so the CI files, `.lockflight/**` and CODEOWNERS need code-owner review
  for the lock check to be a boundary. On GitLab code-owner approval is a paid-tier feature; without it the check is a
  guardrail. Bitbucket and Azure need their own required-reviewer settings on those paths.
- The SDK image is a tag, not a digest, and `dotnet-install.sh` is not checksum-pinned (GitHub's `setup-dotnet` downloads the
  same script). Pin the image by digest if your policy asks for it.
- There is no step summary: the protected files a pull request changes are in the job log, and the result file is kept as an
  artifact where the service keeps artifacts of a failed job.
- A unit repository of a workspace shell: `init --unit-of <shell url> --unit-path <path> --ci-platform <service>` writes the
  job with the shell clone and `--shell`, on every service (see [the shell pointer](ci-hardening.md#the-shell-pointer-preferred)).
  `lockflight workspace sync` vendors GitHub workflows only.

A file `init` writes is never overwritten later, so a template fixed in a newer engine reaches an existing project only when
you delete the file and run `init` again.

Every template's file is a [built-in protected path](configuration.md#built-in-protected-paths). So are the whole
`.gitlab-ci.yml` and `bitbucket-pipelines.yml`: on those services that one file decides whether the job runs at all (a job
defined there replaces an included one of the same name), so all of it is policy, the way `Directory.Build.props` is. The
agent proposes a change to them with [`lockflight propose`](guardrails-and-boundaries.md#proposing-a-protected-change) and a
person applies it. Azure's `azure-pipelines.yml` is not protected: the Lockflight pipeline does not read it.

## Azure Pipelines

`init --ci-platform azure` writes `.azure-pipelines/lockflight-gates.yml`. Your `azure-pipelines.yml`, if there is one, is
left alone.

1. **Create the pipeline.** Pipelines → New pipeline → your repository → Existing Azure Pipelines YAML file →
   `/.azure-pipelines/lockflight-gates.yml`. Name it `lockflight-gates`.
2. **Make it required.** Project settings → Repositories → your repository → Policies → Branch policies → `main` →
   Build validation → add `lockflight-gates`, with trigger **Automatic** and policy requirement **Required**. On Azure Repos
   this policy is what runs the pipeline for a pull request (the `pr:` block in the file applies to GitHub and Bitbucket
   repositories built by Azure Pipelines). For a GitHub repository, make the check required in GitHub's ruleset instead.
3. **Route the policy files to a reviewer.** Branch policies → `main` → Automatically included reviewers → add the people or
   group, set **Required**, and set the path filter to the [paths below](#paths-to-route-to-a-reviewer), separated by `;`
   (for example `/lockflight.yaml;/.lockflight/*;/.azure-pipelines/lockflight-gates.yml;*/Directory.Build.props`).

A pull request build runs the pull request's copy of the YAML, as on GitHub, so a pull request can edit the pipeline that
judges it. The required reviewer on `.azure-pipelines/lockflight-gates.yml` is what catches that.

## Bitbucket Pipelines

`init --ci-platform bitbucket` writes `bitbucket-pipelines.yml` when the repository has none. It runs the step on every pull
request and on pushes to `main`.

**A repository that already has `bitbucket-pipelines.yml`:** Bitbucket reads that one file and cannot include another file of
the same repository, so `init` leaves it as it is and writes the job to `.lockflight/ci/bitbucket-pipelines.yml`. Merge it by
hand: copy `clone: depth: full` (the base commit must be in the clone), the `lockflight-gates` step under
`definitions: steps:`, and add `- step: *lockflight-gates` to your `pull-requests` and `branches: main` pipelines. Commit the
result; `init` then sees the job in `bitbucket-pipelines.yml` and writes no copy again.

1. **Turn on Pipelines:** Repository settings → Pipelines → Settings → Enable Pipelines.
2. **Make it a merge check.** Repository settings → Branch restrictions → `main` → Merge settings (merge checks) → require a
   minimum number of successful builds for the last commit with no failed builds. **Only Bitbucket Premium enforces merge
   checks** ("Prevent a merge with unresolved merge checks"); on Free and Standard a failing check is a warning the person
   merging can ignore, so the job is advisory.
3. **Route the policy files to a reviewer.** Bitbucket Cloud has no built-in path-based required reviewers. Add default
   reviewers and a minimum-approvals merge check (again enforced only on Premium), or use a code-owners app from the
   Atlassian Marketplace with the [paths below](#paths-to-route-to-a-reviewer). Check your edition's current documentation:
   Bitbucket Data Center differs.

## GitLab CI/CD

`init --ci-platform gitlab` writes the job to `.gitlab/ci/lockflight-gates.yml`. When the repository has no `.gitlab-ci.yml`,
it writes one that only includes the job. When it has one, it leaves it as it is and prints what to add:

```yaml
include:
  - local: /.gitlab/ci/lockflight-gates.yml
```

The job runs in the `test` stage, in merge request pipelines and on pushes to the default branch. If your `.gitlab-ci.yml`
lists its own `stages:`, keep `test` among them.

1. **Make it required.** Settings → Merge requests → Merge checks → **Pipelines must succeed** (every tier). Leave "Skipped
   pipelines are considered successful" off: a commit message with `[skip ci]` would otherwise merge without the gates.
   GitLab requires the *pipeline* to succeed, not this job, so a merge request that removes the job or its include from
   `.gitlab-ci.yml` passes. That is why the whole `.gitlab-ci.yml` is protected and needs a code owner.
2. **Route the policy files to a code owner.** Add a `CODEOWNERS` file (at the root, in `docs/` or in `.gitlab/`) with the
   [paths below](#paths-to-route-to-a-reviewer), then protect `main` with **Code owner approval** required. Code owner
   approval is **GitLab Premium or Ultimate**; on Free, CODEOWNERS shows owners but blocks nothing.

The base on a merge request is `CI_MERGE_REQUEST_DIFF_BASE_SHA`: the commit the merge request's diff starts from, on the
target branch. It can be older than the target branch's tip, so a policy change merged after the merge request branched off
applies once the branch is rebased.

## Paths to route to a reviewer

The [CODEOWNERS list in CI hardening](ci-hardening.md#what-you-add-codeowners-for-the-protected-paths) covers every built-in
protected path; replace `/.github/workflows/lockflight-gates.yml` and `/.github/CODEOWNERS` with your service's files, and add
your manifest's own `protected:` globs:

| Service | Files to add |
|---|---|
| Azure Pipelines | `/.azure-pipelines/lockflight-gates.yml` |
| Bitbucket Pipelines | `/bitbucket-pipelines.yml`, `/.lockflight/ci/` |
| GitLab CI/CD | `/.gitlab-ci.yml`, `/.gitlab/ci/lockflight-gates.yml`, and the `CODEOWNERS` file itself |

## Guardrail or boundary

As on GitHub ([guardrails and boundaries](guardrails-and-boundaries.md)), the job is a **boundary** only where the service
enforces it; everywhere else it is a guardrail: it reports, and a person decides.

| Service | Boundary when | Otherwise |
|---|---|---|
| Azure Pipelines | the pipeline is a **Required** build validation policy on `main` and the policy files have a **Required** reviewer by path | advisory |
| Bitbucket Pipelines | **Premium**, with the successful-build merge check enforced and the policy files routed to a required reviewer | advisory: on Free and Standard merge checks do not block |
| GitLab CI/CD | "Pipelines must succeed" is on, skipped pipelines are not counted as successful, and code owner approval is required (**Premium or Ultimate**) | on Free the pipeline blocks a merge, but a merge request that edits `.gitlab-ci.yml` to drop the job is judged by its own edit, with no code owner to stop it: advisory against a hostile change |

And until the first run on each service has been checked (the warning at the top), treat the job as advisory everywhere.

## Next

- [CI hardening](ci-hardening.md): the GitHub workflow, CODEOWNERS and approvals in CI
- [Guardrails and boundaries](guardrails-and-boundaries.md): which controls are boundaries, and under what conditions
- [Commands](commands.md#setup): `init --ci-platform`
