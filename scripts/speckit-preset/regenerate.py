"""Regenerates the Lockflight tasks template from Spec Kit's stock template, so a Spec Kit upgrade is a re-run, not a merge.

Usage: python3 scripts/speckit-preset/regenerate.py <stock tasks-template.md> > plugins/lockflight/speckit-preset/templates/tasks-template.md
It lives outside the preset so `specify preset add` does not copy this maintainer script into user projects.
The stock template comes from `specify init` at the version pinned in preset.yml (`.specify/templates/tasks-template.md`).
Fails loudly when an anchor is missing, so upstream changes are noticed.
"""

import re
import sys

STOCK_TESTS = "**Tests**: The examples below include test tasks. Tests are OPTIONAL - only include them if explicitly requested in the feature specification."
LOCKFLIGHT_TESTS = """**Tests (Lockflight)**: Tests are MANDATORY and written FIRST. For every user story, test tasks precede implementation tasks; the tests must fail (red) before implementation starts. Every task line cites the requirement IDs it serves, e.g. `(FR-001, FR-003)`.

**Red/green (Lockflight, .NET)**: when a story adds public members, its first task declares them as stubs whose bodies are `throw new NotImplementedException();`. Test tasks then write xUnit v3 tests tagged `[Trait("Requirement", "FR-001")]` against the stubs; `lockflight redgreen red` must accept the red before implementation tasks start, and `lockflight redgreen green` closes the story."""

REPLACEMENTS = [
    (STOCK_TESTS, LOCKFLIGHT_TESTS),
    ("## Format: `[ID] [P?] [Story] Description`", "## Format: `[ID] [P?] [Story] (FR-xxx, ...) Description`"),
    ("(OPTIONAL - only if tests requested)", "(MANDATORY — write first, ensure they FAIL)"),
]

# Spec Kit is MIT-licensed: the derived template keeps the attribution (THIRD-PARTY-NOTICES.md has the full text).
NOTICE = "<!-- Derived from the Spec Kit tasks template, Copyright GitHub, Inc., MIT License (https://github.com/github/spec-kit). -->\n"
FRONT_MATTER = re.compile(r"\A---\n.*?\n---\n", re.DOTALL)

TASK_IN_STORY = re.compile(r"^(- \[ \] T\d{3} (?:\[P\] )?\[US\d+\] )(?!\(FR-)", re.MULTILINE)


def main() -> None:
    text = open(sys.argv[1], encoding="utf-8").read()
    for stock, lockflight in REPLACEMENTS:
        if stock not in text:
            sys.exit(f"anchor not found in the stock template: {stock[:60]}…")
        text = text.replace(stock, lockflight)
    text, count = TASK_IN_STORY.subn(r"\1(FR-xxx) ", text)
    if count == 0:
        sys.exit("no story task lines found in the stock template")
    front_matter = FRONT_MATTER.match(text)
    if front_matter is None:
        sys.exit("no front matter found in the stock template")
    text = text[: front_matter.end()] + "\n" + NOTICE + text[front_matter.end():]
    sys.stdout.write(text)


main()
