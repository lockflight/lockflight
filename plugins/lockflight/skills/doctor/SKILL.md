---
name: doctor
description: Diagnose a Lockflight setup — manifest, engine pin, linked engine, gitignore, Claude settings, Git Bash and Spec Kit prerequisites — and explain each fix. Use when hooks block unexpectedly, after cloning, after a Lockflight upgrade, or when the user asks whether Lockflight is set up correctly.
---

# Lockflight doctor

1. Run `.lockflight/bin/lockflight doctor` from the project root (the directory holding `lockflight.yaml`). That is the
   engine the hooks run; it works whatever way Lockflight was installed.
   If the engine itself is missing (recovery mode) or differs from the pinned version ("running engine X but the project pins
   Y"), the only things the guard allows are reads and exactly `dotnet tool restore && dotnet lockflight link` (nothing
   chained before or after it) — run exactly that first, then doctor. When the pin is a development build (`dev` in the
   version, such as `0.2.0-alpha.4.dev.…`, installed from source), that restore cannot find it: ask the user to run `~/.lockflight/bin/lockflight update` in
   the project in a separate terminal window (not with `!` in Claude Code: that runs inside the session and is refused).
   If the pin cannot be read (`dotnet-tools.json` is malformed), that file is protected: show the user the error and let them
   fix it.
2. Report each line. For `FAIL`/`warn`, give the fix shown after `→`.
3. Apply fixes that are Lockflight commands yourself (`dotnet tool restore`, `dotnet lockflight link`,
   `.lockflight/bin/lockflight init`). A fix the doctor marks as a person's step — `update`, or a re-pin of the protected tool
   pin — is the user's: give them the command to run in a separate terminal window (not with `!` in Claude Code: that runs
   inside the session and is refused). Fixes that need an edit to a protected file (for example a broken `lockflight.yaml`) are
   for the user: show the exact location from the error (`lockflight.yaml:line:column`), write the corrected file in your
   scratch directory, run `.lockflight/bin/lockflight propose lockflight.yaml --from <file>` and give the user the `git apply`
   command it prints.
4. When the user asks whether the safeguards actually work (after an upgrade, on a new machine), run
   `.lockflight/bin/lockflight doctor --canaries`. It plants violations in a temporary scratch project, never in this one, and
   prints each as `caught`, `MISSED` or `BROKEN`. Report every line that is not `caught` as it is: a MISSED control is not
   working in this engine build, and BROKEN means the canary could not judge. `--canaries=full` also builds a small generated
   project (tens of seconds).
