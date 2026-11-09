"""The catch benchmark's bookkeeping (run.sh calls it; nothing here builds or runs tests).

  catch.py baseline --repo <checkout> --base <sha> --head <sha> --exit-code <n> --elapsed-ms <n> --results <dir>
                    --threshold-file <coverage.json> --out <file>
      The plain CI's verdict: FAIL when `dotnet test` failed, or when the tests executed less than the threshold's share of
      the production lines the pull request added or changed (Cobertura reports under --results); PASS otherwise, and PASS
      when no changed line is coverable.
  catch.py record --sample … --case … --category … --kind … --what … --judge <judge.json> --verdict <verdict.json>
                  --evidence <evidence.json> --baseline <baseline.json>
      One pull request's result as a JSON line on stdout.
  catch.py line              one result line from stdin, as text
  catch.py summarize --date <yyyy-mm-dd> --out-md <file> --out-json <file> [--runner <text>] [--engine <text>] <jsonl>…
      The results as Markdown and JSON.
"""

import argparse
import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

TEST_PATH = re.compile(r"(^|/)tests?/|\.Tests?/")


def changed_lines(repo, base, head):
    """Production C# lines the head adds or changes against the merge base: {relative path: {line numbers}}."""
    merge_base = subprocess.run(["git", "-C", repo, "merge-base", base, head], capture_output=True, text=True,
                                check=True).stdout.strip()
    diff = subprocess.run(["git", "-C", repo, "diff", "-U0", "--no-color", "--no-renames", merge_base, head, "--", "*.cs"],
                          capture_output=True, text=True, check=True).stdout
    lines, path = {}, None
    for row in diff.splitlines():
        if row.startswith("+++ "):
            target = row[4:]
            path = None if target == "/dev/null" else target[2:]
            if path and TEST_PATH.search(path):
                path = None
        elif row.startswith("@@") and path:
            match = re.match(r"@@ -\S+ \+(\d+)(?:,(\d+))? @@", row)
            start, count = int(match.group(1)), int(match.group(2) or "1")
            lines.setdefault(path, set()).update(range(start, start + count))
    return lines


def coverage_hits(results):
    """Line hits from every Cobertura report under the folder: {absolute or relative file name: {line: hits}}."""
    hits = {}
    for folder, _, files in os.walk(results):
        for name in files:
            if not name.endswith(".xml"):
                continue
            try:
                root = ET.parse(os.path.join(folder, name)).getroot()
            except ET.ParseError:
                continue
            if root.tag != "coverage":
                continue
            sources = [s.text or "" for s in root.iter("source")]
            for cls in root.iter("class"):
                filename = cls.get("filename", "")
                names = [filename] + [os.path.join(s, filename) for s in sources]
                for line in cls.iter("line"):
                    number, count = int(line.get("number")), int(line.get("hits", "0"))
                    for full in names:
                        bucket = hits.setdefault(full.replace("\\", "/"), {})
                        bucket[number] = max(bucket.get(number, 0), count)
    return hits


def baseline(args):
    try:
        with open(args.threshold_file, encoding="utf-8") as config:
            threshold = float(json.load(config).get("patchThreshold", 80))
    except (OSError, ValueError):
        threshold = 80.0
    changed = changed_lines(args.repo, args.base, args.head)
    hits = coverage_hits(args.results) if os.path.isdir(args.results) else {}
    coverable = covered = 0
    for path, numbers in changed.items():
        report = {}
        for name, lines in hits.items():
            if name == path or name.endswith("/" + path):
                for number, count in lines.items():
                    report[number] = max(report.get(number, 0), count)
        for number in numbers:
            if number in report:
                coverable += 1
                covered += report[number] > 0
    percent = round(100.0 * covered / coverable, 1) if coverable else None
    if args.exit_code != 0:
        verdict, why = "FAIL", f"dotnet test exited {args.exit_code}"
    elif percent is not None and percent < threshold:
        verdict, why = "FAIL", f"patch coverage {percent}% < {threshold:g}%"
    else:
        verdict = "PASS"
        why = f"tests passed, patch coverage {percent}% >= {threshold:g}%" if percent is not None else \
            "tests passed, no coverable changed line"
    result = {"verdict": verdict, "why": why, "testExitCode": args.exit_code, "elapsedMs": args.elapsed_ms,
              "patchCoverage": percent, "coverableLines": coverable, "coveredLines": covered, "threshold": threshold,
              "coverageReports": bool(hits)}
    with open(args.out, "w", encoding="utf-8") as out:
        json.dump(result, out, indent=2)


def load(path):
    try:
        with open(path, encoding="utf-8") as source:
            return json.load(source)
    except (OSError, ValueError):
        return None


def record(args):
    judge = load(args.judge) or {}
    verdict = load(args.verdict) or {}
    evidence = load(args.evidence) or {}
    base = load(args.baseline) or {}
    checks = []
    for check in verdict.get("checks") or evidence.get("checks") or []:
        skipped_required = check.get("verdict") == "SKIPPED" and check.get("requirement") == "required"
        if check.get("verdict") not in ("PASS", "N/A", "SKIPPED") or skipped_required:
            checks.append({"id": check.get("id"), "verdict": check.get("verdict"), "requirement": check.get("requirement"),
                           "reason": check.get("reason")})
    steps = []
    for step in ((evidence.get("gates") or {}).get("steps") or []):
        if step.get("verdict") not in ("PASS", "N/A", "SKIPPED"):
            steps.append({"id": step.get("id"), "verdict": step.get("verdict"), "reason": step.get("reason")})
    protected = []
    for step in ((evidence.get("gates") or {}).get("steps") or []):
        if step.get("id") == "policy.protected":
            protected = [m for m in re.findall(r"[\w./-]+", json.dumps(step.get("details") or step.get("why") or ""))
                         if "/" in m or "." in m][:10]
    lockflight = verdict.get("verdict") or evidence.get("verdict") or "ERROR"
    try:
        with open(os.path.join(os.path.dirname(args.verdict), "comment.md"), encoding="utf-8") as comment:
            assertion_free = len(set(re.findall(r"`([^`]+) \(assertion-free\)`", comment.read())))
    except OSError:
        assertion_free = 0
    result = {
        "sample": args.sample, "case": args.case, "category": args.category, "kind": args.kind, "what": args.what,
        "lockflight": {
            "verdict": lockflight,
            "contexts": verdict.get("contexts") or evidence.get("contexts"),
            "blocking": [c for c in checks if c["requirement"] == "required"],
            "advisory": [c for c in checks if c["requirement"] != "required"],
            "gateSteps": steps,
            "policyLoosened": (evidence.get("policy") or {}).get("loosened") or [],
            "assertionFreeTests": assertion_free,
            "exitCode": judge.get("exitCode"), "elapsedMs": judge.get("elapsedMs"),
        },
        "baseline": base,
    }
    # A pull request held only because its change class requires the review of record, which this benchmark does not run
    # (it would call a model), is neither caught nor passed: the outcome is the reviewer's.
    blocking = result["lockflight"]["blocking"]
    if lockflight == "PASS":
        outcome = "passed"
    elif blocking and all(c["id"] == "review" for c in blocking):
        outcome = "review"
    else:
        outcome = "blocked"
    result["lockflight"]["outcome"] = outcome
    result["lockflight"]["caught"] = outcome == "blocked"
    result["baseline"]["caught"] = base.get("verdict") != "PASS"
    print(json.dumps(result))


def reasons(side):
    codes = [f"{c['id']}:{c.get('reason') or c['verdict']}" for c in side.get("blocking", [])]
    codes += [f"{s['id']}:{s.get('reason') or s['verdict']}" for s in side.get("gateSteps", [])]
    return ", ".join(dict.fromkeys(codes)) or "-"


def advisories(side):
    flags = [f"{c['id']}:{c.get('reason') or c['verdict']}" for c in side.get("advisory", [])]
    if side.get("assertionFreeTests"):
        flags.append(f"{side['assertionFreeTests']} assertion-free test(s) marked in the trace")
    return ", ".join(flags) or "-"


def line(_):
    for row in sys.stdin:
        r = json.loads(row)
        print(f"   lockflight {r['lockflight']['verdict']} ({reasons(r['lockflight'])}; advisory {advisories(r['lockflight'])}) "
              f"in {r['lockflight'].get('elapsedMs', 0) / 1000:.0f} s | plain CI {r['baseline'].get('verdict')} "
              f"({r['baseline'].get('why')}) in {r['baseline'].get('elapsedMs', 0) / 1000:.0f} s")


def seconds(ms):
    return f"{(ms or 0) / 1000:.0f}"


def runner_minutes(path, pull_requests):
    """Billed minutes of the measure jobs: each job's duration rounded up to the minute, as GitHub bills hosted runners."""
    if not path:
        return None
    from datetime import datetime
    jobs = load(path) or []
    billed = 0
    for job in jobs:
        if job.get("started_at") and job.get("completed_at"):
            start = datetime.fromisoformat(job["started_at"].replace("Z", "+00:00"))
            end = datetime.fromisoformat(job["completed_at"].replace("Z", "+00:00"))
            billed += -(-int((end - start).total_seconds()) // 60)
    if not billed:
        return None
    return {"jobs": len(jobs), "billed": billed,
            "perPullRequest": round(billed / pull_requests, 1) if pull_requests else None}


def summarize(args):
    rows = []
    for path in args.results:
        with open(path, encoding="utf-8") as source:
            rows += [json.loads(r) for r in source if r.strip()]
    cheats = [r for r in rows if r["kind"] == "cheat"]
    controls = [r for r in rows if r["kind"] == "control"]
    passed_plain = [r for r in cheats if not r["baseline"]["caught"]]
    caught_of_those = [r for r in passed_plain if r["lockflight"]["caught"]]
    categories = list(dict.fromkeys(r["category"] for r in cheats))
    per_category = []
    for category in categories:
        group = [r for r in cheats if r["category"] == category]
        per_category.append({
            "category": category, "cheats": len(group),
            "lockflight": sum(r["lockflight"]["caught"] for r in group),
            "review": sum(r["lockflight"].get("outcome") == "review" for r in group),
            "plain": sum(r["baseline"]["caught"] for r in group),
            "plainPassed": sum(not r["baseline"]["caught"] for r in group),
            "lockflightOfPlainPassed": sum(r["lockflight"]["caught"] and not r["baseline"]["caught"] for r in group),
        })
    judge_ms = [r["lockflight"].get("elapsedMs") or 0 for r in rows]
    plain_ms = [r["baseline"].get("elapsedMs") or 0 for r in rows]
    summary = {
        "date": args.date, "runner": args.runner, "engine": args.engine,
        "plainCi": "dotnet test with code coverage; FAIL when a test fails or when patch coverage (changed production lines "
                   "the tests executed) is below the threshold in the head's coverage.json (80 on main)",
        "cheats": len(cheats), "controls": len(controls),
        "controlsFalseRed": [f"{r['sample']}/{r['case']}" for r in controls if r["lockflight"]["caught"] or r["baseline"]["caught"]],
        "lockflightCaught": sum(r["lockflight"]["caught"] for r in cheats),
        "lockflightHeldForReview": [f"{r['sample']}/{r['case']}" for r in cheats if r["lockflight"].get("outcome") == "review"],
        "lockflightPassed": [f"{r['sample']}/{r['case']}" for r in cheats if r["lockflight"].get("outcome") == "passed"],
        "plainCaught": sum(r["baseline"]["caught"] for r in cheats),
        "plainPassed": len(passed_plain), "lockflightCaughtOfPlainPassed": len(caught_of_those),
        "catchRateOfPlainPassed": round(100.0 * len(caught_of_those) / len(passed_plain), 1) if passed_plain else None,
        "judgeSecondsTotal": round(sum(judge_ms) / 1000), "plainSecondsTotal": round(sum(plain_ms) / 1000),
        "judgeSecondsMedian": round(sorted(judge_ms)[len(judge_ms) // 2] / 1000) if judge_ms else None,
        "plainSecondsMedian": round(sorted(plain_ms)[len(plain_ms) // 2] / 1000) if plain_ms else None,
        "runnerMinutes": runner_minutes(args.jobs, len(rows)),
        "categories": per_category, "pullRequests": rows,
    }
    with open(args.out_json, "w", encoding="utf-8") as out:
        json.dump(summary, out, indent=2)
        out.write("\n")
    md = [f"# Catch benchmark results, {args.date}", ""]
    md.append(f"Runner: {args.runner or 'unknown'}. Engine: {args.engine or 'unknown'}. Plain CI: {summary['plainCi']}. "
              "Lockflight: `lockflight ci` with the sample's base policy (version 2 defaults, patch coverage on with the "
              "Microsoft collector at 80 %), both status contexts treated as required. Machine-readable: "
              f"[`{os.path.basename(args.out_json)}`]({os.path.basename(args.out_json)}).")
    md.append("")
    md.append(f"- **Cheats:** {len(cheats)}. Lockflight blocked {summary['lockflightCaught']}, held "
              f"{len(summary['lockflightHeldForReview'])} for the review of record (not run here) and passed "
              f"{len(summary['lockflightPassed'])}; the plain CI failed {summary['plainCaught']}.")
    md.append(f"- **Cheats the plain CI passed:** {len(passed_plain)}; Lockflight blocked {len(caught_of_those)} of them"
              + (f" ({summary['catchRateOfPlainPassed']} %)." if passed_plain else "."))
    md.append(f"- **Honest controls:** {len(controls)}; " + (
        "every one passed both." if not summary["controlsFalseRed"] else
        "false red: " + ", ".join(summary["controlsFalseRed"]) + "."))
    md.append(f"- **Wall time:** the judge {summary['judgeSecondsTotal']} s in all (median {summary['judgeSecondsMedian']} s "
              f"per pull request), the plain CI {summary['plainSecondsTotal']} s (median {summary['plainSecondsMedian']} s).")
    minutes = summary["runnerMinutes"]
    if minutes:
        md.append(f"- **Runner minutes:** {minutes['billed']} billed for {minutes['jobs']} jobs (each rounded up to the "
                  f"minute, the engine built once per job), about {minutes['perPullRequest']} per pull request judged "
                  "with both.")
    md += ["", "## By category", "", "| Category | Cheats | Lockflight blocked | Held for review | Plain CI failed | Plain CI passed, Lockflight blocked |",
           "|---|---|---|---|---|---|"]
    for c in per_category:
        md.append(f"| {c['category']} | {c['cheats']} | {c['lockflight']} | {c['review']} | {c['plain']} | "
                  f"{c['lockflightOfPlainPassed']} of {c['plainPassed']} |")
    md += ["", "## Every pull request", "",
           "| Sample | Case | Kind | Lockflight | Blocking reasons | Shown for review | Plain CI | Why | Judge s | Plain s |",
           "|---|---|---|---|---|---|---|---|---|---|"]
    for r in rows:
        lf, pl = r["lockflight"], r["baseline"]
        md.append(f"| {r['sample']} | `{r['case']}` | {r['kind']} | {lf['verdict']} ({lf.get('outcome')}) | {reasons(lf)} | {advisories(lf)} | "
                  f"{pl.get('verdict')} | {pl.get('why')} | {seconds(lf.get('elapsedMs'))} | {seconds(pl.get('elapsedMs'))} |")
    md += ["", "## What each pull request does", "", "| Case | Category | What it does |", "|---|---|---|"]
    for r in dict((r["case"], r) for r in rows).values():
        md.append(f"| `{r['case']}` | {r['category']} | {r['what']} |")
    with open(args.out_md, "w", encoding="utf-8") as out:
        out.write("\n".join(md) + "\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    b = sub.add_parser("baseline")
    for name in ("repo", "base", "head", "results", "threshold-file", "out"):
        b.add_argument("--" + name, required=True)
    b.add_argument("--exit-code", type=int, required=True)
    b.add_argument("--elapsed-ms", type=int, required=True)
    r = sub.add_parser("record")
    for name in ("sample", "case", "category", "kind", "what", "judge", "verdict", "evidence", "baseline"):
        r.add_argument("--" + name, required=True)
    sub.add_parser("line")
    s = sub.add_parser("summarize")
    for name in ("date", "out-md", "out-json"):
        s.add_argument("--" + name, required=True)
    s.add_argument("--runner", default="")
    s.add_argument("--engine", default="")
    s.add_argument("--jobs", default="", help="the measure jobs' start and end times (GitHub API), for runner minutes")
    s.add_argument("results", nargs="+")
    args = parser.parse_args()
    {"baseline": baseline, "record": record, "line": line, "summarize": summarize}[args.command](args)


if __name__ == "__main__":
    main()
