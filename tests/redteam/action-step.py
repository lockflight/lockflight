#!/usr/bin/env python3
"""Prints the script of one step of the CI judge's action, so a suite runs the action's own code rather than a copy of it.

  action-step.py <action.yml> <step name>

The step is found by its exact `name:`, and its `run: |` block is printed without its indentation. Exit 1 when the action has
no such step, or the step runs no script. Run with `python3 -I`.
"""

import sys


def main():
    action, wanted = sys.argv[1], sys.argv[2]
    with open(action, encoding="utf-8") as file:
        lines = file.read().splitlines()
    try:
        start = next(i for i, line in enumerate(lines) if line.strip() == f"- name: {wanted}")
    except StopIteration:
        sys.exit(f"{action}: no step named '{wanted}'")
    step_indent = len(lines[start]) - len(lines[start].lstrip()) + 2
    script = None
    for i in range(start + 1, len(lines)):
        line = lines[i]
        indent = len(line) - len(line.lstrip())
        if line.strip() and indent < step_indent:
            break
        if line.strip() == "run: |" and indent == step_indent:
            script = []
            for body in lines[i + 1:]:
                if body.strip() and len(body) - len(body.lstrip()) <= step_indent:
                    break
                script.append(body[step_indent + 2:])
            break
    if script is None:
        sys.exit(f"{action}: the step '{wanted}' runs no script")
    print("\n".join(script))


if __name__ == "__main__":
    main()
