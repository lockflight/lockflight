#!/bin/sh
# Puts a folder on the user's PATH for new terminals, the way rustup and bun do: one marked line in the startup file of the
# user's shell ($SHELL), added only when that file does not have it yet, so reinstalls and `lockflight update` add nothing.
#   zsh: ${ZDOTDIR:-~}/.zshrc   bash: ~/.bashrc, and ~/.bash_profile on macOS (Terminal starts login shells)
#   fish: ~/.config/fish/conf.d/lockflight.fish   any other shell: ~/.profile
# Only files in the user's home are written. Prints what it changed and how to undo it.
# Usage: scripts/user-path.sh <folder>        (scripts/dev-install.sh runs it; --no-modify-path there skips it)
set -eu
folder="$1"
marker="# added by Lockflight: its engine on PATH"
config="${XDG_CONFIG_HOME:-$HOME/.config}"

# The folder as the startup file spells it: under the home it follows $HOME, so the line still works if the home moves.
case "$folder" in
  "$HOME"/*) spelled="\$HOME/${folder#"$HOME"/}" ;;
  *) spelled="$folder" ;;
esac

case "$(basename "${SHELL:-sh}")" in
  zsh) files="${ZDOTDIR:-$HOME}/.zshrc" ;;
  bash)
    files="$HOME/.bashrc"
    [ "$(uname -s)" != Darwin ] || files="$files
$HOME/.bash_profile" ;;
  fish) files="$config/fish/conf.d/lockflight.fish" ;;
  *) files="$HOME/.profile" ;;
esac

changed=""
# One file per line; a home with spaces stays one path.
old_ifs=$IFS; IFS='
'
for file in $files; do
  if [ -f "$file" ] && grep -qF "$marker" "$file"; then
    continue
  fi
  case "$file" in
    *.fish) line="fish_add_path --global \"$spelled\" $marker" ;;
    *) line="export PATH=\"$spelled:\$PATH\" $marker" ;;
  esac
  mkdir -p "$(dirname "$file")"
  printf '\n%s\n' "$line" >> "$file"
  changed="$changed$file
"
done
IFS=$old_ifs

if [ -z "$changed" ]; then
  echo "lockflight: $folder is already on PATH for new terminals"
  exit 0
fi
echo "lockflight: put $folder on PATH for new terminals, with this line in:"
printf '%s' "$changed" | sed 's/^/  /'
echo "  To undo: delete the line ending in \"$marker\"."
echo "  Open a new terminal to use lockflight by name, or run: export PATH=\"$folder:\$PATH\""
