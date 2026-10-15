# C# code style

Rules for the engine (`engine/`) and the NuGet packages. Formatting is encoded in `.editorconfig`; everything else is checked in
review (and, where possible, by analyzers later). A rule may be departed from only visibly — one line at the code or in the PR —
never silently.

## Structure

1. **One type per file**, named after the type. Files are grouped in **feature folders** (`Hooks/`, `Gates/`, `Paths/`,
   `Config/`, …); a folder that mixes interfaces, results, options and enums is split further.
2. **Enums live in their own files.** The enum has a `<summary>` saying what the set is for; **every member** has a `<summary>`
   and an **explicit value** (`Pass = 0`). A closed set is an enum, never a string.
3. **Enums are never `ToString()`-ed to satisfy a contract** (JSON, CLI output). The contract mapping lives in one place.
4. **Data-shaped types are `record`s; classes are `sealed`** unless something derives from them.
5. **Member order:** constants, fields, constructor, public members, private members, nested types last.
6. **A multi-outcome result is a type,** not a `bool`, `null` or `bool` + reason string.

## Naming

7. **Names carry the logic; comments carry the why.** No comment that restates the code or narrates history. XML docs on
   public members only when they add something the name and type cannot (format, unit, meaning of `null`); none on private
   members or test classes. **Never a ticket number in code.**
8. **A dependency is named for its role** (`IGateRunner gates`, not `IGateRunner _gateRunner`). **No `_` prefix** on fields.
   A constructor that only assigns dependencies is a **primary constructor**.
9. **A variable is named for what it holds**, not for the verb that produced it (`buildResult`, not `built`).
10. **Lambda and `out` parameters get real names** (`glob => …`, not `g => …`); `_` discards stay allowed.
11. **A literal with meaning gets a name** — limits, thresholds, exit codes, file names, protocol values.
12. **`var` only when the type is written on the right** (`new T()`, cast, `as T`); explicit types otherwise, including
    built-ins.
13. **Usings:** `System*` first, no unused usings, no fully qualified type names inline.

## Behaviour

14. **Time is injected** (`TimeProvider`), never `DateTime*.UtcNow/Now` outside the composition root.
15. **`CancellationToken`** on every async method: last parameter, named `cancellationToken`, no default, forwarded everywhere.
16. **`ConfigureAwait(false)`** is not used in the engine (an application); it **is** used in reusable library packages.
17. **Guard clauses protect contracts**; they do not duplicate validation done elsewhere.
18. **A named private method beats a commented block**; extract cohesive steps, not single lines.
19. **Messages say enough to act on:** an error names what failed and what to do next (the engine's stderr is read by an agent
    and by a human).
20. **`Any()` on lazy sequences, `Count` on materialized collections.**
21. **A published version is immutable;** a published signature is a contract (bump, never re-publish). Local test packs use
    `0.0.0-local.<n>` — a version that can never be published — because the .NET tool resolver cache maps a version to the
    first package it saw (this bit us twice).
22. **A pattern introduced in a file is applied to its neighbours** in the area being changed; existing code is evidence, not
    authority.

## Tests

23. **A behaviour change requires tests** for every meaningful branch and failure path.
24. **Test names are one descriptive PascalCase phrase without underscores**, stating the behaviour:
    `AWriteToAProtectedPathIsBlocked`, not `PreToolUse_DeniesWriteToProtectedPath`.

## Before pushing

Read the whole diff for defects (not the summary), re-read the task, fix, then run the tests and look at the result.
Anything in the diff you cannot explain is not ready.
