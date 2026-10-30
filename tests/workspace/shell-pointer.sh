#!/bin/sh
# The shell pointer end to end: a unit repository whose lockflight.yaml points at its workspace shell (shell:) is judged with
# the shell's policy for it, read from the shell's committed lockflight.yaml in a clone (--shell) or in the shell it is checked
# out in; a pull request run cannot go without it.
# Usage: ENGINE=<lockflight binary> tests/workspace/shell-pointer.sh <empty work directory>
set -u
WORK="${1:?work directory}"
ENGINE="${ENGINE:?set ENGINE to a lockflight binary}"
git_() { git -c user.email=t@t -c user.name=t -c init.defaultBranch=main "$@"; }
pass=0; fail=0
expect() { if eval "$2"; then pass=$((pass+1)); echo "ok   $1"; else fail=$((fail+1)); echo "FAIL $1"; fi; }

SHELL_DIR="$WORK/shell"
mkdir -p "$SHELL_DIR" && cd "$SHELL_DIR" || exit 1
printf 'version: 2\nprotected:\n  - "services/orders/contracts/**"\nunits:\n  - path: services/orders\n    kind: none\n' > lockflight.yaml
printf 'services/\n' > .gitignore
git_ init -q && git_ add -A && git_ commit -qm "Shell policy"

UNIT="$SHELL_DIR/services/orders"
mkdir -p "$UNIT" && cd "$UNIT" || exit 1
printf 'version: 2\nshell:\n  url: https://example.com/shell.git\n  unit: services/orders\n' > lockflight.yaml
git_ init -q && git_ add -A && git_ commit -qm "Unit"
mkdir -p contracts && printf 'v2\n' > contracts/orders.txt && git_ add -A && git_ commit -qm "Change a contract"

"$ENGINE" gate run --profile pr --base HEAD~1 > "$WORK/inside.txt" 2>&1
expect "inside its shell, the unit finds the shell and names its commit" 'grep -q "Policy: lockflight.yaml with the workspace shell.s policy for unit services/orders" "$WORK/inside.txt"'
expect "the shell's protected path, re-based, is listed for review" 'grep -q "contracts/orders.txt" "$WORK/inside.txt"'

CLONE="$WORK/clone"
git_ clone -q --no-checkout "$SHELL_DIR" "$CLONE"
"$ENGINE" gate run --profile pr --base HEAD~1 --shell "$CLONE" > "$WORK/foreign.txt" 2>&1 || true
expect "a clone of another repository is refused" 'grep -q "not of the workspace shell" "$WORK/foreign.txt"'
git -C "$CLONE" remote set-url origin https://example.com/shell.git
mv "$UNIT" "$WORK/orders" && cd "$WORK/orders" || exit 1
"$ENGINE" gate run --profile pr --base HEAD~1 > "$WORK/alone.txt" 2>&1; rc=$?
expect "alone, a pull request run without the shell is refused" '[ $rc -eq 4 ] && grep -q "workspace clone --into" "$WORK/alone.txt"'
"$ENGINE" gate run --profile pr --base HEAD~1 --shell "$CLONE" > "$WORK/clone.txt" 2>&1
expect "with --shell, the clone's committed policy applies" 'grep -q "contracts/orders.txt" "$WORK/clone.txt"'
"$ENGINE" workspace clone --into "$WORK/orders/inside" > "$WORK/into.txt" 2>&1; rc=$?
expect "workspace clone refuses a directory inside the repository" "[ $rc -eq 4 ]"

echo "shell pointer: $pass passed, $fail failed"
[ "$fail" -eq 0 ]
