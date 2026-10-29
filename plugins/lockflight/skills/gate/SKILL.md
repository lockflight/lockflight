---
name: gate
description: Run Lockflight's deterministic gates (format, build, tests; with --profile pr also patch coverage, vulnerable packages and secrets) and explain the verdict — PASS, NOT-EARNED, FAIL or ERROR. Use when the user asks to check, verify or gate the code, or before declaring a task done.
---

# Lockflight gate

1. Run `.lockflight/bin/lockflight gate run --profile stop` (or `--profile pr` when the user asks for the full CI set) from the
   project root (the directory holding `lockflight.yaml`; `.lockflight/bin/lockflight` is the engine the hooks run). Add
   `--result artifacts/lockflight/result.json` when the user wants the JSON envelope.
2. Read the last two lines: `Scope: …` (what was judged, such as `N tests`) and `Result: <VERDICT>`.
   - **PASS** — every step passed over a non-empty scope.
   - **N/A** — no step applied to this run (for example `--profile edit` with no changed C# lines); exit 0. Say that nothing
     was judged rather than calling it a pass.
   - **NOT-EARNED** — the gate could not judge (for example zero tests ran, or gitleaks is missing). It is not a pass, and
     it does not say the code is wrong: say what is missing so the step can judge, and never call the task done.
   - **FAIL** — the code has a problem; show the failing step and its first error, then fix the code (never the tests or the
     gate configuration to make it pass).
   - **ERROR** — the tool or environment broke (missing runtime, toolchain, network). Explain the environment problem; do not
     change code to work around it.
   A single step may read **N/A**: it did not apply (for example patch coverage that is not configured). Say so; it is neither
   a pass nor a failure. The stop profile's `projects.affected` row is always N/A: it judges nothing and names the projects the
   uncommitted changes affect, which build and test run (`--full` runs every project; the pr profile always does).
3. Never claim a task is done while the gate is not PASS.
