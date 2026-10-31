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
  same source in the engine. Policy files are restored with `git show` (the committed bytes, as the GitHub action reads them),
  so a checkout that converts line endings cannot change the install script CI hashes. A pull request that changes the lock
  runs `lockflight engine verify-lock` on it; the pull request that adds the **first** lock is not release-checked on these
  platforms (the base's 0.2 engine cannot), so the job prints a warning and the reviewer runs
  `lockflight engine verify-lock .lockflight/engine.lock` before accepting it.
- **`lockflight gate run --profile pr --base <base sha>`**, with the result in `artifacts/lockflight/result.json` (Azure and
  GitLab); Bitbucket runs the [CI judge](#bitbucket-pipelines), which runs the same gates at head and adds the red/green
  evidence. Azure and GitLab run the gates only for now.

| | Azure Pipelines | Bitbucket Pipelines | GitLab CI/CD |
|---|---|---|---|
| File `init` writes | `.azure-pipelines/lockflight-gates.yml`, a pipeline of its own | `bitbucket-pipelines.yml` | `.gitlab/ci/lockflight-gates.yml`, and a `.gitlab-ci.yml` that includes it |
| When the team already has a pipeline file | untouched: this is a separate pipeline | untouched: the job goes to `.lockflight/ci/bitbucket-pipelines.yml` to merge by hand | `.gitlab-ci.yml` untouched: `init` prints the `include` to add |
| Base on a pull request | the tip of `origin/<System.PullRequest.TargetBranch>`, fetched (the gates work out the merge base) | the tip of `origin/$BITBUCKET_PR_DESTINATION_BRANCH` (the judge works out the merge base) | the tip of `origin/$CI_MERGE_REQUEST_TARGET_BRANCH_NAME`, fetched (the gates work out the merge base) |
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

`init --ci-platform bitbucket` writes `bitbucket-pipelines.yml` when the repository has none. It runs the CI judge on every
pull request and on pushes to `main`, in three steps:

- **`judge (runs the pull request's code)`** checks out the pull request's own commit (`$BITBUCKET_COMMIT`; the
  pipeline's checkout has the destination merged in), fetches the destination's tip, restores its policy and runs
  [`lockflight ci --platform bitbucket`](commands.md#ci): red at each red commit, green and the `pr` gates at head, trace.
  The step passes on any verdict and holds no secret of Lockflight's (a unit repository's shell token is the exception: see
  [the shell pointer](ci-hardening.md#the-shell-pointer-preferred)); it clears `artifacts/lockflight-evidence/` and hands its
  evidence to the next steps there as an artifact.
- **`gates`** and **`lockflight/judge`**, in parallel, read that evidence as data with `lockflight ci report`: `gates` is the
  `pr` gates and green at head, `lockflight/judge` the red/green evidence, trace and host tampering. A pull request that
  commits files under `artifacts/lockflight-evidence/` or `artifacts/lockflight-verdict/` is an ERROR in both. Bitbucket has
  one build status for the whole pipeline, not one per step, so a failing step fails the build: `gates` fails with its
  verdict, and `lockflight/judge` prints its verdict but fails the build only when the repository variable
  `LOCKFLIGHT_JUDGE_REQUIRED` is `true` (turn it on when the team produces red commits). A push to `main` runs the judge and
  `gates` only.

No review step is generated: Bitbucket's secured repository variables reach every step, the judge's included, so an API key
belongs only in a deployment environment bound to the review step; add such a step by hand from the GitHub workflow's review
job if you want one. The branch name reaches the engine as a quoted variable, never as shell.

**A repository that already has `bitbucket-pipelines.yml`:** Bitbucket reads that one file and cannot include another file of
the same repository, so `init` leaves it as it is and writes the job to `.lockflight/ci/bitbucket-pipelines.yml`. Merge it by
hand: copy `clone: depth: full` (the base commit must be in the clone), the three steps under `definitions: steps:`, and
the `pull-requests` and `branches: main` pipelines that run them. Commit the result; `init` then sees the job in `bitbucket-pipelines.yml` and writes no copy again.

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

The base on a merge request is the target branch's tip, fetched when the job starts, not `CI_MERGE_REQUEST_DIFF_BASE_SHA`
(the commit the merge request's diff starts from). The diff base can be older than the tip, and a branch cut from an old commit
would bring that commit's engine pin, baselines and analyzer config. The gates work out the merge base for the diff themselves.

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
