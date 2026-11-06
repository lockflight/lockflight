#!/usr/bin/env python3
"""Checks that relative links in the repository's Markdown resolve: the file or directory exists, and an anchor
(#heading) names a heading of the target file, as GitHub renders it.

    python3 -I scripts/check-doc-links.py [file.md ...]   # default: every tracked *.md but the changelog and test fixtures

Links to URLs (http:, https:, mailto:) are not fetched. Code spans and fenced code blocks are skipped. Exit 1 on a broken link.
"""
import os
import re
import subprocess
import sys
import unicodedata
from urllib.parse import unquote

SKIPPED = re.compile(r"(^|/)CHANGELOG\.md$|^engine/tests/|^samples/|^tests/benchmark/|/evals/")
LINK = re.compile(r"!?\[(?:[^\[\]]|\[[^\]]*\])*\]\(\s*<?([^)\s>]+)>?(?:\s+\"[^\"]*\")?\s*\)")
HTML = re.compile(r"""\b(?:href|src|srcset)=["']([^"']+)["']""")
HEADING = re.compile(r"^(#{1,6})\s+(.*?)\s*#*\s*$")
EXPLICIT = re.compile(r"""<a\s+(?:[^>]*\s)?(?:id|name)=["']([^"']+)["']""")
FENCE = re.compile(r"^\s*(```|~~~)")


def slug(text):
    """GitHub's heading anchor: markup dropped, lower case, punctuation removed, spaces to hyphens."""
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", text)
    text = re.sub(r"<[^>]+>", "", text)
    text = text.replace("`", "").replace("*", "").strip().lower()
    kept = []
    for char in text:
        category = unicodedata.category(char)
        if char in " -_" or category[0] in "LN" or category == "Mn":
            kept.append(char)
    return "".join(kept).replace(" ", "-")


def lines_outside_code(path):
    fenced = False
    with open(path, encoding="utf-8") as handle:
        for number, line in enumerate(handle, 1):
            if FENCE.match(line):
                fenced = not fenced
                continue
            if not fenced:
                yield number, line


anchor_cache = {}


def anchors(path):
    if path not in anchor_cache:
        found, seen = set(), {}
        for _, line in lines_outside_code(path):
            match = HEADING.match(line)
            if match:
                base = slug(match.group(2))
                count = seen.get(base, 0)
                seen[base] = count + 1
                found.add(base if count == 0 else f"{base}-{count}")
            found.update(EXPLICIT.findall(line))
        anchor_cache[path] = found
    return anchor_cache[path]


def check(document):
    problems = []
    for number, line in lines_outside_code(document):
        text = re.sub(r"(`+)(?:(?!\1).)+?\1", "", line)
        targets = LINK.findall(text) + [part.split()[0] for value in HTML.findall(text) for part in value.split(",") if part.strip()]
        for target in targets:
            if re.match(r"^[a-z][a-z0-9+.-]*:", target, re.IGNORECASE) or target.startswith("//"):
                continue
            path, _, anchor = target.partition("#")
            path = unquote(path.split("?")[0])
            if path:
                resolved = os.path.normpath(path.lstrip("/") if path.startswith("/") else os.path.join(os.path.dirname(document), path))
                if not os.path.exists(resolved):
                    problems.append(f"{document}:{number}: {target}: no such file")
                    continue
            else:
                resolved = document
            if anchor and resolved.endswith(".md") and os.path.isfile(resolved) and unquote(anchor).lower() not in anchors(resolved):
                problems.append(f"{document}:{number}: {target}: no heading with that anchor in {resolved}")
    return problems


def main(arguments):
    root = subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True, text=True, check=True).stdout.strip()
    os.chdir(root)
    documents = arguments or [
        path for path in subprocess.run(["git", "ls-files", "*.md"], capture_output=True, text=True, check=True).stdout.splitlines()
        if not SKIPPED.search(path)
    ]
    problems = [problem for document in documents for problem in check(document)]
    for problem in problems:
        print(problem, file=sys.stderr)
    print(f"check-doc-links: {len(documents)} documents, {len(problems)} broken links")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
