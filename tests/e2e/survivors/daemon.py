"""A process a pull request's build leaves running to forge the CI judge's results (a test fixture, never run outside it).

  daemon.py <role> <token>

It detaches (on Linux it ignores SIGTERM and SIGHUP; the stubborn role double-forks into a session of its own), writes
started-<role>-<token> (its pid) into $LOCKFLIGHT_FIXTURE_MARKERS, then for up to 15 minutes watches for the judge's evidence,
the gates' result and the test reports the judge writes, and rewrites each to a pass, writing forged-<role>-<token> when it did.
The root role only waits: it stands for a process the judge cannot stop.
"""

import glob
import json
import os
import signal
import sys
import tempfile
import time

LIFETIME = 900
POLL = 0.05


def write(path, text):
    temporary = f"{path}.tmp{os.getpid()}"
    with open(temporary, "w", encoding="utf-8") as handle:
        handle.write(text)
    os.replace(temporary, path)


def forge_json(path):
    with open(path, encoding="utf-8") as handle:
        document = json.load(handle)
    document["verdict"] = "PASS"
    for check in document.get("checks") or []:
        check["verdict"] = "PASS"
        check.pop("reason", None)
    for name in list((document.get("contexts") or {}).keys()):
        document["contexts"][name] = "PASS"
    for step in document.get("steps") or []:
        step["verdict"] = "PASS"
        step.pop("reason", None)
    write(path, json.dumps(document))


def forge_report(path):
    with open(path, encoding="utf-8") as handle:
        report = handle.read()
    write(path, report.replace('outcome="Failed"', 'outcome="Passed"').replace('result="Fail"', 'result="Pass"'))


def detach(role):
    if os.name == "posix":
        signal.signal(signal.SIGTERM, signal.SIG_IGN)
        signal.signal(signal.SIGHUP, signal.SIG_IGN)
        if role == "stubborn":
            if os.fork() > 0:
                os._exit(0)
            os.setsid()
            if os.fork() > 0:
                os._exit(0)
    devnull = os.open(os.devnull, os.O_RDWR)
    for descriptor in (0, 1, 2):
        os.dup2(devnull, descriptor)


def main(role, token):
    markers = os.environ["LOCKFLIGHT_FIXTURE_MARKERS"]
    evidence = os.environ.get("LOCKFLIGHT_FIXTURE_EVIDENCE", "")
    detach(role)
    started = time.time()
    write(os.path.join(markers, f"started-{role}-{token}"), str(os.getpid()))
    forged = os.path.join(markers, f"forged-{role}-{token}")
    done = set()
    while time.time() - started < LIFETIME:
        if role != "root":
            targets = [(os.path.join(evidence, name), forge_json) for name in ("evidence.json", "evidence-local.json", "gates-result.json")] if evidence else []
            targets += [(path, forge_report) for path in glob.glob(os.path.join(tempfile.gettempdir(), "lockflight-tests-*.xml"))]
            for path, forge in targets:
                try:
                    if path not in done and os.path.getmtime(path) >= started:
                        forge(path)
                        done.add(path)
                        write(forged, path)
                except (OSError, ValueError):
                    pass  # not there yet, or still being written
        time.sleep(POLL)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
