<!-- Title: a Conventional Commit, e.g. "fix(guard): block tee into protected paths". It becomes the squash commit. -->

## What and why

<!-- What changes for a user, and why. Link the issue: "Fixes #123". -->

## How it was verified

- [ ] `cd engine && dotnet build && dotnet test`
- [ ] Cross-component suites touched by this change (see [tests/README.md](../tests/README.md))
- [ ] Docs updated (guides, commands, configuration). CHANGELOG.md is written at release time from the titles.

## Guarantees

<!-- Does this add, weaken or change a control? Say whether it is a guardrail or a boundary
     (docs/guides/guardrails-and-boundaries.md). Write "none" if it does not touch one. -->

## Breaking changes

<!-- Manifest keys, command options, exit codes, hook behaviour. Write "none" if there are none. -->
