---
name: doctor
description: Diagnose a Lockflight setup — manifest, engine pin, linked engine, gitignore, Claude settings, Git Bash and Spec Kit prerequisites — and explain each fix. Use when hooks block unexpectedly, after cloning, after a Lockflight upgrade, or when the user asks whether Lockflight is set up correctly.
---

# Lockflight doctor

1. Run `dotnet lockflight doctor` from the repository root. If the engine itself is missing (recovery mode) or differs from the
   pinned version ("running engine X but the project pins Y"), the only things the guard allows are reads and exactly
   `dotnet tool restore && dotnet lockflight link` (nothing chained before or after it) — run exactly that first, then doctor.
   If the pin cannot be read (`dotnet-tools.json` is malformed), that file is protected: show the user the error and let them
   fix it.
2. Report each line. For `FAIL`/`warn`, give the fix shown after `→`.
3. Apply fixes that are Lockflight commands yourself (`dotnet tool restore`, `dotnet lockflight link`, `dotnet lockflight init`).
   Fixes that need an edit to a protected file (for example a broken `lockflight.yaml`) are for the user: show the exact
   location from the error (`lockflight.yaml:line:column`), write the corrected file in your scratch directory, run
   `dotnet lockflight propose lockflight.yaml --from <file>` and give the user the `git apply` command it prints.
