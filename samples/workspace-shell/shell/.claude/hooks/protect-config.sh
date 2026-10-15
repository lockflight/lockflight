#!/bin/sh
# Team hook: block edits to local runtime overrides (a protected-file guard Lockflight's guard can replace).
input=$(cat)
case "$input" in
  *docker-compose.override.yml*|*appsettings.Local.json*)
    echo "Local runtime overrides are maintained by hand." >&2; exit 2 ;;
esac
exit 0
