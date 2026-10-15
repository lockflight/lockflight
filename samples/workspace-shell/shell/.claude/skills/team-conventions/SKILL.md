---
name: team-conventions
description: Acme service conventions (naming, folders, error handling). Use when writing or reviewing service code.
---
- One feature folder per use case under `src/<Service>/Features/`.
- Errors cross service boundaries as problem details, never as exceptions.
- Money is `decimal` with currency; never `double`.
