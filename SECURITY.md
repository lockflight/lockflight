# Security policy

Lockflight is a set of controls around a coding agent, so a way around one of them is a security issue. Examples: a write to a
protected path that the guard misses, a gate that passes when it should not, an approval the agent can forge, or CI policy
that a pull request can change.

## Reporting

> [!IMPORTANT]
> Report privately through
> [GitHub's private vulnerability reporting](https://github.com/lockflight/lockflight/security/advisories/new). Please
> don't open a public issue.

Include the Lockflight version (`dotnet lockflight version`), your OS, and a minimal reproduction.
You can expect an acknowledgement within a week.

## Scope

Lockflight labels each control a **guardrail** or a **boundary**; see
[guardrails and boundaries](docs/guides/guardrails-and-boundaries.md).

- **A bypass of a boundary** is always in scope. Example: CI accepts a change it should reject while the check is required.
- **A bypass of a guardrail** is in scope when it is accidental. Example: an ordinary `sed -i` on a protected file goes
  unnoticed. It is not in scope when it relies on a capability the page already lists as out of reach. Example: an agent
  deliberately writing through a script file.

- **Opening a project runs its engine.** With the plugin enabled, every hook runs the engine linked into the project's
  `.lockflight/bin/`, starting with `SessionStart`. The shim refuses an engine that is committed to the repository, owned by another
  user (outside Windows), different from the SHA-256 `lockflight link` recorded for it, or without such a record. It does not verify the binary against
  the released package. Treat opening an untrusted
  project like running its build: the project's own `.claude/settings.json` hooks can run code too, once you trust the folder.
  A way to make the shim run an engine it should refuse is in scope.

Only the latest prerelease is supported during the preview.
