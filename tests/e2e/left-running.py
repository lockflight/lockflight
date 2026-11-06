"""What the CI judge's sweeps found in a suite's judge runs: the processes the runs left behind.

  left-running.py record <evidence.json> <log>   append the evidence's processes object (or its absence) to the log
  left-running.py summary <log>                  print every run's sweeps and processes left running; exit 1 when a run on
                                                 Linux or Windows did not sweep (the mechanism did not start)

A process the judge stopped does not change its verdict, so the suites only report them; survivors.sh tests the enforcement.
"""

import json
import platform
import sys

EXPECTED = {"Linux": "subreaper", "Windows": "job"}


def record(evidence_path, log_path):
    try:
        with open(evidence_path, encoding="utf-8") as evidence:
            processes = json.load(evidence).get("processes")
    except (OSError, ValueError):
        processes = None
    with open(log_path, "a", encoding="utf-8") as log:
        log.write(json.dumps({"evidence": evidence_path, "processes": processes}) + "\n")


def summary(log_path):
    system = platform.system()
    if system.startswith(("MINGW", "MSYS", "CYGWIN")):
        system = "Windows"
    expected = EXPECTED.get(system)
    try:
        with open(log_path, encoding="utf-8") as log:
            runs = [json.loads(line) for line in log if line.strip()]
    except OSError:
        runs = []
    failures = 0
    left = 0
    reaped = 0
    for index, run in enumerate(runs, 1):
        processes = run["processes"]
        if processes is None:
            print(f"info judge run {index}: no processes object (the judge wrote no evidence)")
            continue
        survivors = processes.get("survivors", [])
        running = [survivor for survivor in survivors if survivor["outcome"] != "exited"]
        exited = len(survivors) - len(running)
        left += len(running) + processes.get("more", 0)
        reaped += exited
        held = processes.get("outputHeld", [])
        print(f"info judge run {index}: {processes['mechanism']}, {processes['sweeps']} sweeps, "
              f"{len(running) + processes.get('more', 0)} left running, {exited} exited by themselves, {len(held)} outputs held")
        for survivor in survivors:
            print(f"info   {survivor['outcome']}: {survivor['command'][:160]} "
                  f"({survivor['region']}, after {survivor['after'][:80]}, image {survivor['image'][:80]})")
        for command in held:
            print(f"info   output held: {command[:160]}")
        if expected and processes["mechanism"] != expected:
            print(f"FAIL judge run {index} did not sweep on {system}: mechanism {processes['mechanism']}, {processes.get('note')}")
            failures += 1
    print(f"info processes left running in {len(runs)} judge runs: {left} (and {reaped} that exited by themselves before a check)")
    return 1 if failures else 0


if __name__ == "__main__":
    if len(sys.argv) == 4 and sys.argv[1] == "record":
        record(sys.argv[2], sys.argv[3])
    elif len(sys.argv) == 3 and sys.argv[1] == "summary":
        sys.exit(summary(sys.argv[2]))
    else:
        sys.exit(__doc__)
