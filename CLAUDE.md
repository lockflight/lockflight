# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this repo is

**Lockflight** — gated, spec-driven delivery for Claude Code agents on .NET: a Claude Code plugin, a C# Native AOT
engine and a Spec Kit preset. Alpha.

- [`docs/architecture.md`](docs/architecture.md) explains the parts and the principles. Read the relevant section before
  proposing a change to how something works.
- [`docs/guides/`](docs/guides/) is the user documentation. A change in behaviour updates the guide that describes it in the
  same pull request.

## How we work

- **Small pull requests, verified locally** before they are opened: `cd engine && dotnet build && dotnet test`, plus the
  cross-component suites the change touches ([`tests/README.md`](tests/README.md)).
- **Branches, commits and pull requests** follow [CONTRIBUTING.md](CONTRIBUTING.md): `<type>/<area>-<what>` branches,
  Conventional Commits with the listed scopes, squash merges with the pull request title as the commit, and the pull request
  template filled in.
- **Commit messages, pull requests, code comments and user-facing text describe the change for a reader of this repository.**
  They do not cite planning documents, internal finding or task IDs, or anything that is not in this repository.
- **Honest claims.** Distinguish guardrails from boundaries; never describe a control as enforced unless the platform actually
  enforces it ([guardrails and boundaries](docs/guides/guardrails-and-boundaries.md)).
- **English** for everything committed.

## C# code conventions (engine)

Follow [`docs/engineering/code-style.md`](docs/engineering/code-style.md). The essentials: one type per file in feature
folders; enums in their own files with a `<summary>` on the enum and every member and explicit values; `sealed` classes and
`record` DTOs; dependencies named for their role, no `_` fields, primary constructors; `var` only when the type is written;
injected time; `CancellationToken` last without a default; test names as one PascalCase phrase without underscores.

## Hard rules

- **No third-party private code or data** in this repository.
- **Human-only commands** (`approve`, `baseline`, `redgreen reset`, `redgreen amend`, `review dismiss`, `update`) are never run by an agent in
  a real project; tests run them in temporary directories only.
- **Outward-facing actions need the maintainer's approval:** pushing, publishing packages, creating or changing repositories,
  opening pull requests on other repositories.
