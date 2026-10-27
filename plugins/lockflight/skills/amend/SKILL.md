---
name: amend
description: Help the user let one locked test of the open red cycle change (a wrong expectation, an API that changed) — in this session by typing /lockflight:amend, or with lockflight redgreen amend in a separate terminal window.
disable-model-invocation: true
argument-hint: <Class.Method> --reason "<why the test must change>"
---

# Lockflight amend (human decision)

While a red cycle is open, the test projects are locked. When one test turns out to be wrong (its expectation contradicts the
spec, or the API it uses had to change), the user can let exactly that test change. The amend unlocks the file that declares
the test until the next `redgreen red`, which must show the test failing in production code again. Who, when and why are
recorded, and trace and the review's pull request summary show the amend. Only the user decides it, in one of two ways:

- **In this session:** the user types `/lockflight:amend <Class.Method> --reason <why>` (the reason comes last). The engine's
  hook, not you, shows them the test, the file it unlocks and the other tests in that file, with a one-time code. They type
  `/lockflight:amend <Class.Method> <code>`, and the engine records the amend.
- **In a separate terminal window:** `lockflight redgreen amend <Class.Method> --reason "<why>"` (not with `!` in Claude
  Code: that runs inside the session and is refused).

## Never

- Never amend by yourself: never run `lockflight redgreen amend`, never type `/lockflight:amend`, never edit files under
  `.lockflight/`, and never change a locked test any other way.
- Never type, guess, repeat or pre-fill a code, and never ask the user to paste one to you.
- Never assume an amend happened: if you are reading this, the engine's hook did not handle the command. Check with
  `.lockflight/bin/lockflight redgreen status`, which lists each amend and the file it unlocks.

## What to do

1. Show the user the test, what it expects, and why that is wrong: quote the requirement it contradicts or the API change.
2. Tell them the exact command to type, with the test as `Class.Method` and a reason in one line, for example
   `/lockflight:amend ShippingTests.FirstKilogramCostsFive --reason "FR-001 says 5.50, not 5.00"`.
3. Once `redgreen status` shows the amend, change only that test, as the reason says. Then run
   `.lockflight/bin/lockflight redgreen red`: the amended test must fail in production code again (RED_STUB or RED_ASSERT).
   Then go on to green.
