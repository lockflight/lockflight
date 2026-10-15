#!/bin/sh
# Installs Lockflight from this checkout, with no package feed and no token:
#   1. packs the engine for this machine into a local feed (~/.lockflight/feed),
#   2. installs it under ~/.lockflight/tools/<version>, outside any project's global.json (an SDK 8 pin cannot restore the
#      RID-specific package), and points ~/.lockflight/bin/lockflight at it,
#   3. adds the marketplace and installs the plugin at user scope (needed for sessions started inside a child repo of a
#      workspace shell: Claude Code loads project settings only from the directory it starts in).
# Usage: scripts/dev-install.sh [--no-plugin]        Needs the .NET 10 SDK; the plugin step needs `claude` and git access to the
# lockflight/lockflight repository.
set -eu
REPO=$(cd "$(dirname "$0")/.." && pwd)
HOME_DIR="${LOCKFLIGHT_HOME:-$HOME/.lockflight}"
plugin=true
for option in "$@"; do
  case "$option" in
    --no-plugin) plugin=false ;;
    *) echo "unknown option: $option (use --no-plugin)" >&2; exit 4 ;;
  esac
done

case "$(uname -s)-$(uname -m)" in
  Darwin-arm64) rid=osx-arm64 ;;
  Darwin-x86_64) rid=osx-x64 ;;
  Linux-x86_64) rid=linux-x64 ;;
  Linux-aarch64|Linux-arm64) rid=linux-arm64 ;;
  *) echo "unsupported platform $(uname -s)-$(uname -m); on Windows run this from Git Bash with rid=win-x64 support (not yet)" >&2; exit 1 ;;
esac

# The version follows the commit, so the same code always gets the same version and a project's pin goes stale only when the
# code changed. A checkout with uncommitted changes gets the time instead.
if [ -z "$(git -C "$REPO" status --porcelain --untracked-files=no 2>/dev/null)" ]; then
  stamp=$(TZ=UTC git -C "$REPO" log -1 --format=%cd --date=format-local:%Y%m%d%H%M%S)
else
  stamp=$(date -u +%Y%m%d%H%M%S)
fi
# A development build is numbered after the last release (the plugin's version, raised at each release) so that it sorts
# above that release and below the next one, and moving a project to the next release is an upgrade:
#   after the prerelease 0.2.0-alpha.4: 0.2.0-alpha.4.dev.<stamp>   (below 0.2.0-alpha.5, 0.2.0-beta.1 and 0.2.0)
#   after the release 0.2.0:            0.2.1-0.dev.<stamp>         (below any 0.2.1 prerelease, 0.2.1 and 0.3.0)
released=$(sed -n 's/^ *"version": *"\([^"]*\)".*/\1/p' "$REPO/plugins/lockflight/.claude-plugin/plugin.json" | head -n 1)
case "$released" in
  [0-9]*.[0-9]*.[0-9]*-*) version="$released.dev.$stamp" ;;
  [0-9]*.[0-9]*.[0-9]*)
    major=${released%%.*}; rest=${released#*.}; minor=${rest%%.*}; patch=${rest#*.}
    version="$major.$minor.$((patch + 1))-0.dev.$stamp" ;;
  *) echo "cannot read the released version from plugins/lockflight/.claude-plugin/plugin.json" >&2; exit 1 ;;
esac
feed="$HOME_DIR/feed"
tools="$HOME_DIR/tools/$version"
mkdir -p "$feed" "$HOME_DIR/bin"
# `lockflight update` pulls this checkout and runs this script again.
printf '%s\n' "$REPO" > "$HOME_DIR/source"

if [ -x "$tools/lockflight" ]; then
  echo "lockflight: $version is already installed"
else
  echo "lockflight: packing $version for $rid"
  (cd "$REPO/engine" \
    && dotnet pack src/Lockflight.Cli -c Release -r "$rid" -o "$feed" -p:Version="$version" -nologo -v q \
    && dotnet pack src/Lockflight.Cli -c Release -o "$feed" -p:Version="$version" -nologo -v q) > "$HOME_DIR/pack.log" 2>&1 \
    || { echo "pack failed; see $HOME_DIR/pack.log" >&2; exit 1; }

  echo "lockflight: installing into $tools"
  # From the Lockflight home, so no project's global.json picks the SDK.
  (cd "$HOME_DIR" && dotnet tool install Lockflight.Cli --tool-path "$tools" --version "$version" --add-source "$feed") \
    > "$HOME_DIR/install.log" 2>&1 || { echo "install failed; see $HOME_DIR/install.log" >&2; exit 1; }
fi
ln -sf "$tools/lockflight" "$HOME_DIR/bin/lockflight"
"$HOME_DIR/bin/lockflight" version > /dev/null || { echo "the installed engine does not run" >&2; exit 1; }

if [ "$plugin" = true ]; then
  if command -v claude > /dev/null; then
    echo "lockflight: installing the plugin at user scope"
    claude plugin marketplace list 2>/dev/null | grep -q lockflight || claude plugin marketplace add lockflight/lockflight
    claude plugin marketplace update lockflight > /dev/null 2>&1 || true
    if claude plugin list 2>/dev/null | grep -q 'lockflight@lockflight'; then
      claude plugin update lockflight@lockflight
    else
      claude plugin install lockflight@lockflight --scope user
    fi
  else
    echo "lockflight: claude not found; install the plugin later with: claude plugin install lockflight@lockflight --scope user" >&2
  fi
fi

# lockflight update prints its own next steps.
[ -n "${LOCKFLIGHT_UPDATING:-}" ] && exit 0

cat <<EOF

Lockflight $version is installed: $HOME_DIR/bin/lockflight
Next, in your project (the workspace shell root, or the repository root):
  $HOME_DIR/bin/lockflight init            # or: init --profile standard
  $HOME_DIR/bin/lockflight doctor
The project's tool pin will name $version, which only this machine's feed has: teammates install it the same way.
A project set up with an earlier version: run $HOME_DIR/bin/lockflight update there (it re-pins and links it).
EOF
