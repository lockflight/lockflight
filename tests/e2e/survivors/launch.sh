#!/bin/sh
# Started by the test project's build when the fixture is armed: leaves daemons running the way a pull request's build could
# (setsid and nohup, a double fork, a container, and with LOCKFLIGHT_FIXTURE_ROOT a root process through sudo), each waiting for
# the judge to write something it can forge. Waits until each has started, so the judge's sweep finds them running.
here=$(cd "$(dirname "$0")" && pwd)
m="$LOCKFLIGHT_FIXTURE_MARKERS"
token=$$-$(date +%s)
wait_for() { i=0; while [ ! -s "$1" ] && [ "$i" -lt 100 ]; do sleep 0.1; i=$((i + 1)); done; }
setsid nohup python3 "$here/daemon.py" exec "$token" > /dev/null 2>&1 &
python3 "$here/daemon.py" stubborn "$token" > /dev/null 2>&1 &
wait_for "$m/started-exec-$token"
wait_for "$m/started-stubborn-$token"
if [ -n "${LOCKFLIGHT_FIXTURE_DOCKER:-}" ]; then
  docker run -d --label lockflight-fixture=1 alpine:3.20 sleep 600 > "$m/started-docker-$token" 2> /dev/null || rm -f "$m/started-docker-$token"
fi
if [ -n "${LOCKFLIGHT_FIXTURE_ROOT:-}" ]; then
  sudo -n env LOCKFLIGHT_FIXTURE_MARKERS="$m" setsid python3 "$here/daemon.py" root "$token" > /dev/null 2>&1 &
  wait_for "$m/started-root-$token"
fi
exit 0
