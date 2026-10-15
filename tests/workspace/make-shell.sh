#!/bin/sh
# Materializes samples/workspace-shell as a real workspace shell: the shell is a git repo, and each child is its own git repo
# cloned beneath it (ignored by the shell). Usage: tests/workspace/make-shell.sh <empty-or-missing destination>
set -eu
REPO=$(cd "$(dirname "$0")/../.." && pwd)
DEST="${1:?destination directory}"
[ ! -e "$DEST" ] || [ -z "$(ls -A "$DEST")" ] || { echo "destination is not empty: $DEST" >&2; exit 1; }
mkdir -p "$DEST"
cp -R "$REPO/samples/workspace-shell/shell/." "$DEST/"
commit() { git -C "$1" add -A && git -C "$1" -c user.email=sample@example.com -c user.name=sample commit -qm "$2"; }
git -C "$DEST" init -q -b main
commit "$DEST" "Acme workspace shell"
for child in services/orders services/billing web/admin; do
  mkdir -p "$DEST/$child"
  # cp, not tar: Git Bash's tar reads a Windows destination (D:\…) as a remote host.
  cp -R "$REPO/samples/workspace-shell/children/$child/." "$DEST/$child/"
  find "$DEST/$child" -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
  git -C "$DEST/$child" init -q -b main
  printf 'bin/\nobj/\nnode_modules/\ndist/\n' > "$DEST/$child/.gitignore"
  commit "$DEST/$child" "Initial $child"
done
echo "$DEST"
