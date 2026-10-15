#!/bin/sh
# Team hook (domain rule): message contracts change only through a spec that lists their consumers.
input=$(cat)
case "$input" in
  *"/Contracts/"*)
    echo "Contracts change only through a spec (constitution I). Add the spec first." >&2; exit 2 ;;
esac
exit 0
