#!/bin/sh
# scripts/user-path.sh in a temporary home (with a space) for each shell: the first run adds one marked line to the shell's
# startup file, a second run (a reinstall or `lockflight update`) adds none, and the line puts the folder on PATH.
# Usage: sh tests/install/user-path.sh        (POSIX sh; macOS, Linux)
HERE=$(cd "$(dirname "$0")" && pwd); SCRIPT="$HERE/../../scripts/user-path.sh"
WORK=$(mktemp -d); pass=0; fail=0
marker="# added by Lockflight: its engine on PATH"
ok() { pass=$((pass+1)); echo "ok   $1"; }
bad() { fail=$((fail+1)); echo "FAIL $1"; }
for shell in zsh bash fish sh; do
  home="$WORK/home $shell"; mkdir -p "$home"
  case "$shell" in
    zsh) files="$home/.zshrc" ;;
    bash) files="$home/.bashrc"; [ "$(uname -s)" != Darwin ] || files="$files|$home/.bash_profile" ;;
    fish) files="$home/.config/fish/conf.d/lockflight.fish" ;;
    sh) files="$home/.profile" ;;
  esac
  HOME="$home" SHELL="/bin/$shell" XDG_CONFIG_HOME="" ZDOTDIR="" sh "$SCRIPT" "$home/.lockflight/bin" > "$WORK/first" 2>&1
  second=$(HOME="$home" SHELL="/bin/$shell" XDG_CONFIG_HOME="" ZDOTDIR="" sh "$SCRIPT" "$home/.lockflight/bin" 2>&1)
  old_ifs=$IFS; IFS='|'
  for file in $files; do
    count=$(grep -cF "$marker" "$file" 2>/dev/null || echo 0)
    if [ "$count" = 1 ]; then ok "$shell: one line in ${file#"$home"/} after two runs"; else bad "$shell: $count lines in $file"; fi
  done
  IFS=$old_ifs
  if printf '%s' "$second" | grep -q "already on PATH"; then ok "$shell: the second run changes nothing"; else bad "$shell: second run said: $second"; fi
  if grep -q "To undo" "$WORK/first"; then ok "$shell: the first run says how to undo it"; else bad "$shell: no undo in: $(cat "$WORK/first")"; fi
  if [ "$shell" = sh ] || [ "$shell" = bash ]; then
    startup="$home/.profile"; [ "$shell" = sh ] || startup="$home/.bashrc"
    got=$(HOME="$home" PATH=/usr/bin:/bin sh -c ". \"\$1\"; printf '%s' \"\$PATH\"" sh "$startup")
    case "$got" in "$home/.lockflight/bin:"*) ok "$shell: the line puts the folder first on PATH" ;; *) bad "$shell: PATH is $got" ;; esac
  fi
done
rm -rf "$WORK"
echo "passed=$pass failed=$fail"
[ "$fail" = 0 ]
