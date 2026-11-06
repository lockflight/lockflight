#!/usr/bin/env python3
"""Collects the performance measurements of one machine into one JSON file, and renders one or more of them as Markdown.

    python3 -I tests/benchmark/perf-summary.py collect --out perf.json --label <os> --engine-version <v> --image <image> \
        [--hooks hook-latency.json] [--stop stop-gate.json] [--judge judge-times.txt]
    python3 -I tests/benchmark/perf-summary.py table perf-a.json [perf-b.json ...]

The inputs are what tests/benchmark/hook-latency.sh (JSON_OUT), tests/gates/incremental-timing.sh (JSON_OUT) and
tests/e2e/ci-judge.sh (JUDGE_TIMES) write. A missing input is left out. The table goes to standard output.
"""
import argparse
import datetime
import json
import os
import sys


def collect(args):
    result = {
        "label": args.label,
        "date": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%d"),
        "image": args.image,
        "engine_version": args.engine_version,
    }
    if args.hooks and os.path.isfile(args.hooks):
        with open(args.hooks, encoding="utf-8") as source:
            result["hooks"] = json.load(source)
    if args.stop and os.path.isfile(args.stop):
        with open(args.stop, encoding="utf-8") as source:
            result["stop_gate"] = json.load(source)
    if args.judge and os.path.isfile(args.judge):
        runs = []
        with open(args.judge, encoding="utf-8") as source:
            for line in source:
                parts = line.split()
                if len(parts) == 4:
                    runs.append({"sample": parts[0], "branch": parts[1], "exit": int(parts[2]), "ms": int(parts[3])})
        result["ci_judge"] = runs
    with open(args.out, "w", encoding="utf-8") as out:
        json.dump(result, out, indent=2)


def seconds(ms):
    return f"{ms / 1000:.1f} s"


def table(paths):
    machines = []
    for path in paths:
        with open(path, encoding="utf-8") as source:
            machines.append(json.load(source))
    machines.sort(key=lambda m: m["label"])
    header = "| | " + " | ".join(m["label"] for m in machines) + " |"
    rule = "|---|" + "---|" * len(machines)
    lines = ["| Machine | Date | Runner image | Engine |", "|---|---|---|---|"]
    lines += [f"| {m['label']} | {m['date']} | {m['image']} | {m['engine_version']} |" for m in machines]

    cases = []
    for m in machines:
        for case in m.get("hooks", {}).get("cases", []):
            key = (case["mode"], case["case"])
            if key not in cases:
                cases.append(key)
    if cases:
        lines += ["", "**PreToolUse hook, p50 / p95 in ms**", "", header, rule]
        for mode, name in cases:
            cells = []
            for m in machines:
                found = [c for c in m.get("hooks", {}).get("cases", []) if (c["mode"], c["case"]) == (mode, name)]
                cells.append(f"{found[0]['p50_ms']:.1f} / {found[0]['p95_ms']:.1f}" if found else "")
            lines.append(f"| {mode}: {name} | " + " | ".join(cells) + " |")

    stops = [(c["change"], c["mode"]) for m in machines[:1] for c in m.get("stop_gate", {}).get("cases", [])]
    for m in machines:
        for c in m.get("stop_gate", {}).get("cases", []):
            if (c["change"], c["mode"]) not in stops:
                stops.append((c["change"], c["mode"]))
    if stops:
        runs = sorted({m["stop_gate"]["runs"] for m in machines if "stop_gate" in m})
        lines += ["", f"**Stop gate on a ten-project solution, median of {', '.join(map(str, runs))} runs (whole run)**", "",
                  header, rule]
        for change, mode in stops:
            cells = []
            for m in machines:
                found = [c for c in m.get("stop_gate", {}).get("cases", []) if (c["change"], c["mode"]) == (change, mode)]
                cells.append(seconds(found[0]["median_ms"]) if found else "")
            lines.append(f"| {change} change, {'--full' if mode == 'full' else 'incremental'} | " + " | ".join(cells) + " |")

    judged = []
    for m in machines:
        for index, run in enumerate(m.get("ci_judge", [])):
            key = (index, run["sample"], run["branch"], run["exit"])
            if key not in judged:
                judged.append(key)
    if judged:
        lines += ["", "**CI judge (`lockflight ci`), wall time per run, in the order the suite runs them**", "", header, rule]
        verdicts = {0: "PASS", 1: "FAIL", 5: "NOT-EARNED"}
        for index, sample, branch, code in judged:
            cells = []
            for m in machines:
                runs = m.get("ci_judge", [])
                hit = runs[index] if index < len(runs) and (runs[index]["sample"], runs[index]["branch"]) == (sample, branch) else None
                cells.append(seconds(hit["ms"]) if hit else "")
            lines.append(f"| {sample}, {branch} ({verdicts.get(code, f'exit {code}')}) | " + " | ".join(cells) + " |")
    print("\n".join(lines))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    gather = commands.add_parser("collect")
    gather.add_argument("--out", required=True)
    gather.add_argument("--label", required=True)
    gather.add_argument("--engine-version", required=True)
    gather.add_argument("--image", required=True)
    gather.add_argument("--hooks")
    gather.add_argument("--stop")
    gather.add_argument("--judge")
    render = commands.add_parser("table")
    render.add_argument("files", nargs="+")
    args = parser.parse_args()
    if args.command == "collect":
        collect(args)
    else:
        table(args.files)


if __name__ == "__main__":
    sys.exit(main())
