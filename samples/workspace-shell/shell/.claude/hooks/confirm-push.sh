#!/bin/sh
# Team hook: never push or commit without the user.
input=$(cat)
case "$input" in
  *"git push"*|*"git commit"*) echo "Ask the user before committing or pushing." >&2; exit 2 ;;
esac
exit 0
