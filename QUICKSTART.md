# Quickstart: a first green verdict

The shortest path to a first `PASS` from the CI judge, on a new .NET solution, in about two minutes of machine time. You
make one red commit (a failing test and a stub), one commit that makes it pass, and run the judge on the branch. The judge
re-runs both and gives the verdict. The same command runs in the GitHub Action.

> [!NOTE]
> **The CI judge ships in Lockflight 0.3.0, which is not released yet.** The latest prerelease on nuget.org
> (`0.2.0-alpha.9`) has the gates workflow but not the judge, `init --ci-only` or `init --native`. Until 0.3.0 is out,
> install from source as below and run the judge on your machine. The GitHub workflow `init` writes for a development build
> runs only when started by hand and cannot install that build, so the [GitHub steps](#4-on-github) apply from 0.3.0 on.

**Measured:** 1 minute 17 seconds of machine time from `git clone` to `PASS`, on an Apple M1 Pro with the .NET 10.0.102 SDK
and an empty NuGet package cache: 51 s to clone and install the engine from source, 6 s to create the solution and run
`init`, 20 s for the judge. Typing the commands and the network add to that; a slower machine or a cold `dotnet` first run
take longer.

## What you need

- macOS or Linux, with git and the **.NET 10 SDK** (Windows: [install on Windows](docs/guides/getting-started.md#install-on-windows)).
- **[gitleaks](https://github.com/gitleaks/gitleaks)** on `PATH` (for example `brew install gitleaks`). The judge's secrets
  step needs it; without it that step, and so the verdict, is `NOT-EARNED`. The GitHub Action downloads its own.
- No Claude Code and no API key: this path is the [CI-only install level](docs/guides/install-levels.md#ci-only).

## 1. Install the engine

```sh
git clone https://github.com/lockflight/lockflight
cd lockflight
scripts/dev-install.sh --no-plugin     # the engine into ~/.lockflight/bin/lockflight; adds it to PATH for new terminals
cd ..
```

`--no-plugin` skips the Claude Code plugin, which the CI-only level does not use. `--no-modify-path` leaves your shell's
startup file alone. The rest of this page calls the engine as `~/.lockflight/bin/lockflight`.

## 2. Create a solution and set up Lockflight

Use a new directory. On your own repository, skip the `dotnet new` lines and run `init` at its root.

```sh
mkdir discount-demo && cd discount-demo
git init -b main
dotnet new sln -n Demo
dotnet new classlib -n Demo -o src/Demo
dotnet new xunit -n Demo.Tests -o tests/Demo.Tests
dotnet sln add src/Demo tests/Demo.Tests
dotnet add tests/Demo.Tests reference src/Demo
dotnet new gitignore
git add -A && git commit -m "A new solution"

~/.lockflight/bin/lockflight init --ci-only
git add -A && git commit -m "Set up Lockflight"
```

`init --ci-only` writes `lockflight.yaml` (the policy), the engine pin, `.gitignore` entries and
`.github/workflows/lockflight-gates.yml`, and nothing for Claude Code. The judge reads the policy from the target branch, so
it has to be committed on `main` before the feature branch.

## 3. Make a red commit, then make it green

On a feature branch, commit the new test together with a stub that compiles but does not do the work. The commit carries a
`Lockflight-Red: <cycle>` trailer; the cycle is a positive whole number.

```sh
git switch -c feat/discount

cat > src/Demo/Pricing.cs <<'EOF'
namespace Demo;

public static class Pricing
{
    public static decimal Discount(decimal total) => throw new NotImplementedException();
}
EOF

cat > tests/Demo.Tests/PricingTests.cs <<'EOF'
namespace Demo.Tests;

public class PricingTests
{
    [Fact]
    public void TenPercentOffAboveOneHundred() => Assert.Equal(108m, Pricing.Discount(120m));
}
EOF

git add -A && git commit -m "test: ten percent off above 100" --trailer "Lockflight-Red: 1"
```

Now the implementation, in a commit of its own, with the test file unchanged:

```sh
cat > src/Demo/Pricing.cs <<'EOF'
namespace Demo;

public static class Pricing
{
    public static decimal Discount(decimal total) => total > 100m ? total * 0.9m : total;
}
EOF

git commit -am "feat: ten percent off above 100"
```

Run the judge on the branch, against `main`:

```sh
~/.lockflight/bin/lockflight ci
```

```text
PASS head d1e623b, class behaviour (interim), 1 red commits, policy from c2b10dc
  gates             PASS
  lockflight/judge  PASS
  PASS       red                    required 1 cycles, 1 tests, each red in production code
  PASS       tests.sinceRed         required 1 unchanged, 0 amends accepted
  PASS       tests.existingChanged  advisory no existing test changed
  PASS       tests.hostTampering    required no code in the test assemblies that could tamper with the test host
  PASS       green                  required 2 tests, 2 test cases run, 0 failed
  N/A        trace                  off      no spec found for this pull request
  PASS       gates                  required policy.protected PASS · format PASS · quality.suppressions PASS · docs.references N/A · build PASS · ef.pendingModelChanges N/A · test PASS · coverage.patch N/A · security.deps PASS · security.secrets PASS
  SKIPPED    review                 advisory the report joins the review job's result
  NOT-EARNED approvals              advisory not judged by this engine version yet
```

That is the first green: the judge checked out the red commit, saw the new test fail inside production code (the stub's
`NotImplementedException`), then saw the same test, unchanged, pass at head, with the gates passing under `main`'s policy.
`lockflight ci` refuses uncommitted changes, and it writes its evidence to `.lockflight/local/ci/last.json`.

**See it say no.** Put the test and the implementation in the same red commit and run the judge again:

```sh
git switch -c feat/fake main
git checkout feat/discount -- src tests
git add -A && git commit -m "test and code together" --trailer "Lockflight-Red: 1"
~/.lockflight/bin/lockflight ci        # FAIL: GREEN_BEFORE, the test passes before the implementation exists
```

Without any red commit the verdict is `NOT-EARNED`: nothing showed that the new tests could fail.

## 4. On GitHub

From 0.3.0 on, with a project set up from the released engine:

1. Push `main` with the commit `init` made, then push the feature branch and open a pull request. The workflow's `judge` job
   runs the same `lockflight ci` with the engine and the policy of the target branch, and two checks appear: `gates` (the
   gates and green at head) and `lockflight/judge` (red, tests since red, host tampering, trace, review, existing tests
   changed; approvals are listed, not judged yet). A comment on the pull
   request shows the verdict and the table above.
2. Make `gates` a required check, and `lockflight/judge` too once the team makes red commits.
3. Route the policy and the workflow to a code owner. Without that, a pull request can change the workflow GitHub runs for
   it: [CI hardening](docs/guides/ci-hardening.md).
4. For a review of record in CI, set the repository variable `LOCKFLIGHT_REVIEW` to `true` and the secret
   `ANTHROPIC_API_KEY`. The reviewer runs the pull request's code, so use a spend-limited key scoped to CI.

On Bitbucket, `init --ci-only --ci-platform bitbucket` writes the pipeline instead; it has not yet run on a live Bitbucket
workspace. GitLab and Azure Pipelines templates are experimental: [CI on other platforms](docs/guides/ci-other-platforms.md).

## Next

- **Your own repository:** the same steps at its root. Tests that already fail go in a baseline, merged in its own pull
  request first ([CI hardening](docs/guides/ci-hardening.md)).
- **A spec:** with a feature's `specs/<id>/spec.md`, the judge also checks that every requirement has a test that went red
  and then green ([the spec contract](docs/guides/spec-contract.md)).
- **Help on the developer's machine:** `init --native` or the Claude Code plugin ([install levels](docs/guides/install-levels.md)).
  In the full lifecycle, `lockflight redgreen red` checks the new tests and makes the red commit itself.
- **What a verdict can promise:** [guardrails and boundaries](docs/guides/guardrails-and-boundaries.md).
