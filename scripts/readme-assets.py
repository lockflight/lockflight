"""Regenerates the README art in docs/assets from Levi's pixel map. Keep BODY, PAL and POSES in sync with
engine/src/Lockflight.Cli/Mascot/MascotArt.cs. Usage: python3 scripts/readme-assets.py docs/assets"""
import os
import sys

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)

BODY = [
    "......CCCC......",
    "....CCCAACCC....",
    "...CCCCCCCCCC...",
    "..DBBBBBBBBBBD..",
    "..BBBBBBBBBBBB..",
    "..BBBBBBBBBBBB..",
    "..BBBBBBBBBBBB..",
    "..BBBBBKKBBBBB..",
    "..BBBBLWWLBBBB..",
    "..BBBBLWWLBBBB..",
    ".BBBBLLLLLLBBBB.",
    ".BBBLLLLLLLLBBDD",
    "..BBLLLLLLLLBDDD",
    "...DD......DD...",
]
PAL = {"C": "#14808F", "A": "#F2A33A", "D": "#7A3F1A", "B": "#C0682F", "K": "#141414", "W": "#FFFFFF", "L": "#F0C08E",
       "P": "#C9A06A", "G": "#2F8F5B", "R": "#C9483B", "Q": "#3A2410", "S": "#5B6B75"}
OPEN = [(4, 7, 1, 1, "W"), (5, 7, 1, 1, "K"), (4, 8, 2, 1, "K"), (10, 7, 1, 1, "W"), (11, 7, 1, 1, "K"), (10, 8, 2, 1, "K")]
FLAG = [(17, 1, 1, 12, "P"), (15, 10, 2, 1, "B")]
POSES = {
    "on-duty": OPEN,
    "pass": [(5, 7, 1, 1, "K"), (4, 8, 1, 1, "K"), (6, 8, 1, 1, "K"), (10, 7, 1, 1, "K"), (9, 8, 1, 1, "K"), (11, 8, 1, 1, "K")]
    + FLAG + [(18, 1, 4, 5, "G"), (18, 3, 1, 1, "W"), (19, 4, 1, 1, "W"), (20, 3, 1, 1, "W"), (21, 2, 1, 1, "W")],
    "not-earned": [(4, 8, 2, 1, "K"), (10, 8, 2, 1, "K"), (11, 6, 2, 1, "D"), (16, 0, 6, 6, "A"), (16, 6, 1, 1, "A"), (15, 7, 1, 1, "A"),
                   (18, 1, 2, 1, "Q"), (20, 2, 1, 1, "Q"), (19, 3, 1, 1, "Q"), (19, 5, 1, 1, "Q")],
    "fail": [(3, 6, 1, 1, "K"), (4, 7, 1, 1, "K"), (4, 8, 2, 1, "K"), (12, 6, 1, 1, "K"), (11, 7, 1, 1, "K"), (10, 8, 2, 1, "K")]
    + FLAG + [(18, 1, 4, 5, "R"), (18, 2, 1, 1, "W"), (20, 2, 1, 1, "W"), (19, 3, 1, 1, "W"), (18, 4, 1, 1, "W"), (20, 4, 1, 1, "W")],
    "blocked": OPEN + [(s * 2, 11, 2, 2, "R" if s % 2 == 0 else "W") for s in range(10)] + [(20, 9, 2, 7, "S")],
}


def pixels(pose):
    grid = [["."] * 22 for _ in range(16)]
    for r, row in enumerate(BODY):
        for c, ch in enumerate(row):
            grid[r + 2][c] = ch
    for x, y, w, h, col in POSES[pose]:
        for r in range(y, y + h):
            for c in range(x, x + w):
                grid[r][c] = col
    return grid


def rects(pose, ox=0, oy=0, scale=1):
    """Horizontal runs of one color as rects, offset and scaled."""
    out = []
    for r, row in enumerate(pixels(pose)):
        c = 0
        while c < 22:
            ch = row[c]
            if ch == ".":
                c += 1
                continue
            start = c
            while c < 22 and row[c] == ch:
                c += 1
            out.append(f'<rect x="{ox + start * scale}" y="{oy + r * scale}" width="{(c - start) * scale}" height="{scale}" fill="{PAL[ch]}"/>')
    return "".join(out)


def write(name, body):
    with open(os.path.join(OUT, name), "w") as f:
        f.write(body)


LABELS = {"on-duty": "on duty", "pass": "PASS: a green flag", "not-earned": "NOT-EARNED: a question",
          "fail": "FAIL: a red flag", "blocked": "BLOCKED: the barrier is down"}
for pose in POSES:
    write(f"levi-{pose}.svg",
          f'<svg xmlns="http://www.w3.org/2000/svg" width="264" height="192" viewBox="0 0 22 16" shape-rendering="crispEdges" role="img">'
          f'<title>Levi, {LABELS[pose]}</title>{rects(pose)}</svg>\n')

# Avatar: Levi on a tile, square.
write("levi-avatar.svg",
      '<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256" shape-rendering="crispEdges" role="img">'
      '<title>Levi, the Lockflight lock-keeper</title><rect width="256" height="256" rx="56" fill="#0B1822" shape-rendering="geometricPrecision"/>'
      + rects("on-duty", 32, 16, 12) + "</svg>\n")

FONT = "ui-sans-serif, -apple-system, 'Segoe UI', 'Helvetica Neue', sans-serif"
MONO = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace"


def banner(dark):
    bg, title, accent, body, ring = ("#0B1822", "#FFFFFF", "#7FE3EC", "#B7C9D1", "#7FE3EC") if dark else ("#F4F7F6", "#0E1F2B", "#0E6E7B", "#3A4A54", "#14808F")
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="400" viewBox="0 0 1280 400" role="img">'
        f'<title>Lockflight: LLMs propose. Checks decide.</title>'
        f'<defs><radialGradient id="g1" cx="12%" cy="100%" r="70%"><stop offset="0" stop-color="#14808F" stop-opacity="{0.55 if dark else 0.18}"/><stop offset="1" stop-color="#14808F" stop-opacity="0"/></radialGradient>'
        f'<radialGradient id="g2" cx="85%" cy="10%" r="60%"><stop offset="0" stop-color="#F2A33A" stop-opacity="{0.35 if dark else 0.16}"/><stop offset="1" stop-color="#F2A33A" stop-opacity="0"/></radialGradient></defs>'
        f'<rect width="1280" height="400" rx="24" fill="{bg}"/><rect width="1280" height="400" rx="24" fill="url(#g1)"/><rect width="1280" height="400" rx="24" fill="url(#g2)"/>'
        f'<circle cx="1010" cy="200" r="250" fill="none" stroke="{ring}" stroke-opacity="0.18" stroke-width="2"/>'
        f'<text x="88" y="168" font-family="{FONT}" font-size="96" font-weight="800" letter-spacing="-3" fill="{title}">Lockflight</text>'
        f'<text x="92" y="232" font-family="{FONT}" font-size="38" font-weight="700" fill="{accent}">LLMs propose. Checks decide.</text>'
        f'<text x="92" y="288" font-family="{FONT}" font-size="22" fill="{body}">Gated, spec-driven delivery for Claude Code agents on .NET.</text>'
        f'<g shape-rendering="crispEdges">{rects("pass", 850, 70, 15)}</g></svg>\n')


write("banner-dark.svg", banner(True))
write("banner-light.svg", banner(False))


def social_preview():
    """The repository's social preview (1280x640), rendered to social-preview.png by a headless browser."""
    return (
        '<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="640" viewBox="0 0 1280 640">'
        '<defs><radialGradient id="s1" cx="0%" cy="100%" r="80%"><stop offset="0" stop-color="#14808F" stop-opacity="0.55"/><stop offset="1" stop-color="#14808F" stop-opacity="0"/></radialGradient>'
        '<radialGradient id="s2" cx="85%" cy="0%" r="60%"><stop offset="0" stop-color="#F2A33A" stop-opacity="0.35"/><stop offset="1" stop-color="#F2A33A" stop-opacity="0"/></radialGradient></defs>'
        '<rect width="1280" height="640" fill="#0B1822"/><rect width="1280" height="640" fill="url(#s1)"/><rect width="1280" height="640" fill="url(#s2)"/>'
        '<circle cx="1010" cy="320" r="250" fill="none" stroke="#7FE3EC" stroke-opacity="0.18" stroke-width="2"/>'
        f'<text x="88" y="288" font-family="{FONT}" font-size="96" font-weight="800" letter-spacing="-3" fill="#FFFFFF">Lockflight</text>'
        f'<text x="92" y="352" font-family="{FONT}" font-size="38" font-weight="700" fill="#7FE3EC">LLMs propose. Checks decide.</text>'
        f'<text x="92" y="408" font-family="{FONT}" font-size="22" fill="#B7C9D1">Gated, spec-driven delivery for Claude Code agents on .NET.</text>'
        f'<g shape-rendering="crispEdges">{rects("pass", 850, 190, 15)}</g></svg>\n')


write("social-preview.svg", social_preview())


def flight(dark):
    bg, ink, muted, line = ("#0B1822", "#E6EEF2", "#9FB2BC", "#24384A") if dark else ("#FFFFFF", "#0E1F2B", "#4A5A64", "#D5DEE2")
    steps = [("Spec", "you approve", "#14808F"), ("Tasks", "you approve", "#14808F"), ("Red", "tests fail first", "#C9483B"),
             ("Green", "tests pass, nothing deleted", "#2F8F5B"), ("Trace", "every requirement tested", "#14808F"),
             ("Review", "findings need a failing test", "#14808F"), ("PR", "same checks in CI", "#F2A33A")]
    w, gap, base = 120, 22, 270
    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" width="1040" height="360" viewBox="0 0 1040 360" role="img">'
             f'<title>How a change rises: spec, tasks, red, green, trace, review, pull request, each opened by a check</title>'
             f'<rect width="1040" height="360" rx="20" fill="{bg}" stroke="{line}"/>']
    for i, (name, check, color) in enumerate(steps):
        x = 40 + i * (w + gap)
        h = 70 + i * 26
        y = base - h
        parts.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="10" fill="{color}" fill-opacity="{0.22 if dark else 0.14}"/>')
        parts.append(f'<rect x="{x}" y="{y}" width="{w}" height="8" rx="4" fill="{color}"/>')
        parts.append(f'<text x="{x + w / 2}" y="{y + 38}" text-anchor="middle" font-family="{FONT}" font-size="20" font-weight="700" fill="{ink}">{name}</text>')
        words = check.split(" ")
        lines = [" ".join(words[:2]), " ".join(words[2:])] if len(words) > 2 else [check]
        for j, text in enumerate(lines):
            parts.append(f'<text x="{x + w / 2}" y="{base + 30 + j * 20}" text-anchor="middle" font-family="{FONT}" font-size="14" fill="{"#C47A12" if check == "you approve" else muted}">{text}</text>')
        if i < len(steps) - 1:
            parts.append(f'<rect x="{x + w + gap / 2 - 3}" y="{y - 20}" width="6" height="{h + 20}" rx="3" fill="#F2A33A"/>')
    parts.append(f'<g shape-rendering="crispEdges">{rects("pass", 44, 28, 5)}</g>')
    parts.append("</svg>\n")
    return "".join(parts)


write("lock-flight-light.svg", flight(False))
write("lock-flight-dark.svg", flight(True))

# Terminal still (dark in both themes, like a terminal).
term = [
    '<svg xmlns="http://www.w3.org/2000/svg" width="1040" height="300" viewBox="0 0 1040 300" role="img">',
    '<title>The guard blocks an agent edit to lockflight.yaml; the Stop gate keeps a turn with a failing test from ending</title>',
    '<rect width="1040" height="300" rx="16" fill="#0B1822"/>',
    '<circle cx="28" cy="26" r="7" fill="#C9483B"/><circle cx="50" cy="26" r="7" fill="#F2A33A"/><circle cx="72" cy="26" r="7" fill="#2F8F5B"/>',
    f'<g shape-rendering="crispEdges">{rects("blocked", 28, 66, 4)}{rects("fail", 28, 186, 4)}</g>',
]
rows = [
    (82, "#7A8C96", "# the agent tries to switch the gates off"),
    (110, "#CFE0E6", "⏺ Bash  echo 'stop: {gate: false}' >> lockflight.yaml"),
    (138, "#F28B7F", "⎿ BLOCKED · lockflight.yaml is protected: a human changes it, not the agent"),
    (202, "#7A8C96", "# the agent says “done” while a test is red"),
    (230, "#CFE0E6", "⏺ Stop gate  build PASS 2.7 s · test FAIL 7.3 s  OrderTotals_IncludeTax"),
    (258, "#F2A33A", "⎿ FAIL · the turn can’t end until the failing test passes"),
]
for y, color, text in rows:
    text = text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
    term.append(f'<text x="136" y="{y}" font-family="{MONO}" font-size="17" fill="{color}">{text}</text>')
term.append("</svg>\n")
write("demo-terminal.svg", "".join(term))

# The same scene, animated: lines appear in order, Levi reacts, the scene holds, then loops (CSS keyframes, no script).
LOOP = 14
STEPS = [  # (start second, element)
    (0.4, f'<text x="136" y="82" font-family="{MONO}" font-size="17" fill="#7A8C96"># the agent tries to switch the gates off</text>'),
    (1.4, f'<text x="136" y="110" font-family="{MONO}" font-size="17" fill="#CFE0E6">⏺ Bash  echo \'stop: {{gate: false}}\' &gt;&gt; lockflight.yaml</text>'),
    (2.6, f'<g shape-rendering="crispEdges">{rects("blocked", 28, 66, 4)}</g>'),
    (2.6, f'<text x="136" y="138" font-family="{MONO}" font-size="17" fill="#F28B7F">⎿ BLOCKED · lockflight.yaml is protected: a human changes it, not the agent</text>'),
    (5.0, f'<text x="136" y="202" font-family="{MONO}" font-size="17" fill="#7A8C96"># the agent says “done” while a test is red</text>'),
    (6.0, f'<text x="136" y="230" font-family="{MONO}" font-size="17" fill="#CFE0E6">⏺ Stop gate  build PASS 2.7 s · test FAIL 7.3 s  OrderTotals_IncludeTax</text>'),
    (7.4, f'<g shape-rendering="crispEdges">{rects("fail", 28, 186, 4)}</g>'),
    (7.4, f'<text x="136" y="258" font-family="{MONO}" font-size="17" fill="#F2A33A">⎿ FAIL · the turn can’t end until the failing test passes</text>'),
]
css = []
body = []
for i, (start, element) in enumerate(STEPS):
    on = round(start / LOOP * 100, 2)
    css.append(f"@keyframes s{i}{{0%,{on}%{{opacity:0}}{min(on + 2, 90)}%,92%{{opacity:1}}100%{{opacity:0}}}}.s{i}{{opacity:0;animation:s{i} {LOOP}s linear infinite}}")
    body.append(f'<g class="s{i}">{element}</g>')
write("demo-terminal-animated.svg",
      '<svg xmlns="http://www.w3.org/2000/svg" width="1040" height="300" viewBox="0 0 1040 300" role="img">'
      '<title>The guard blocks an agent edit to lockflight.yaml; the Stop gate keeps a turn with a failing test from ending</title>'
      f'<style>{"".join(css)}@media (prefers-reduced-motion: reduce){{{"".join(f".s{i}" + "{animation:none;opacity:1}" for i in range(len(STEPS)))}}}</style>'
      '<rect width="1040" height="300" rx="16" fill="#0B1822"/>'
      '<circle cx="28" cy="26" r="7" fill="#C9483B"/><circle cx="50" cy="26" r="7" fill="#F2A33A"/><circle cx="72" cy="26" r="7" fill="#2F8F5B"/>'
      + "".join(body) + "</svg>\n")
# Diagrams for the guides: light and dark variants, shown through <picture>.
def theme(dark):
    return {"bg": "#0B1822", "ink": "#E6EEF2", "muted": "#9FB2BC", "line": "#24384A", "card": "#10222F", "teal": "#7FE3EC",
            "fill": 0.22} if dark else {"bg": "#FFFFFF", "ink": "#0E1F2B", "muted": "#4A5A64", "line": "#D5DEE2",
                                         "card": "#F4F7F6", "teal": "#0E6E7B", "fill": 0.12}


def esc(text):
    return text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def text(x, y, value, color, size=15, weight=400, anchor="start", font=FONT):
    return (f'<text x="{x}" y="{y}" text-anchor="{anchor}" font-family="{font}" font-size="{size}" font-weight="{weight}" '
            f'fill="{color}">{esc(value)}</text>')


def card(t, x, y, w, h, color, title, lines, mono=()):
    """A rounded card with a coloured top bar, a bold title and centred lines (indexes in mono use the monospace font)."""
    out = [f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="10" fill="{color}" fill-opacity="{t["fill"]}"/>',
           f'<rect x="{x}" y="{y}" width="{w}" height="6" rx="3" fill="{color}"/>',
           text(x + w / 2, y + 32, title, t["ink"], 18, 700, "middle")]
    for i, value in enumerate(lines):
        out.append(text(x + w / 2, y + 56 + i * 20, value, t["muted"], 13 if i in mono else 14, 400, "middle", MONO if i in mono else FONT))
    return "".join(out)


def arrow(x1, y1, x2, y2, color, dashed=False):
    dash = ' stroke-dasharray="5 5"' if dashed else ""
    return (f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{color}" stroke-width="2.5"{dash} '
            f'marker-end="url(#head)"/>')


def head(color):
    return (f'<defs><marker id="head" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto">'
            f'<path d="M0 0L10 5L0 10z" fill="{color}"/></marker></defs>')


def hook_flow(dark):
    t = theme(dark)
    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" width="1040" height="340" viewBox="0 0 1040 340" role="img">',
             '<title>A hook call: Claude Code runs the hook shim, the shim runs the pinned engine, and the engine allows with exit 0 '
             'or blocks with exit 2</title>', head(t["muted"]),
             f'<rect width="1040" height="340" rx="20" fill="{t["bg"]}" stroke="{t["line"]}"/>',
             card(t, 30, 40, 220, 140, "#14808F", "Claude Code", ["SessionStart · PreToolUse", "UserPromptExpansion", "Stop"]),
             arrow(252, 110, 286, 110, t["muted"]),
             card(t, 290, 40, 220, 140, "#14808F", "Hook shim", ["lockflight-hook", "finds the project from", "CLAUDE_PROJECT_DIR"], (0, 2)),
             arrow(512, 110, 546, 110, t["muted"]),
             card(t, 550, 40, 220, 140, "#F2A33A", "Engine", [".lockflight/bin/lockflight", "the pinned version", "makes every decision"], (0,)),
             arrow(772, 90, 806, 72, t["muted"]), arrow(772, 130, 806, 148, t["muted"]),
             card(t, 810, 30, 200, 76, "#2F8F5B", "Allow", ["exit 0"]),
             card(t, 810, 114, 200, 76, "#C9483B", "Block", ["exit 2, reason on stderr"]),
             arrow(360, 182, 250, 226, t["muted"], True), arrow(440, 182, 560, 226, t["muted"], True),
             card(t, 60, 230, 340, 80, "#5B6B75", "No lockflight.yaml", ["does nothing: harmless in other projects"]),
             card(t, 430, 230, 580, 80, "#C9483B", "Engine missing or stale, configuration unreadable",
                  ["recovery mode: reads and the exact restore command only"]),
             "</svg>\n"]
    return "".join(parts)


write("hook-flow-light.svg", hook_flow(False))
write("hook-flow-dark.svg", hook_flow(True))


def trust(dark):
    t = theme(dark)
    left = ["The guard on protected paths", "The Stop gate", "Red/green evidence and the trace", "The review of record",
            "Approvals recorded locally"]
    right = ["CI with the base branch's policy, as a required check", "Presence-gated signed approvals, verified in CI",
             "Merge, when the agent has its own identity"]
    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" width="1040" height="400" viewBox="0 0 1040 400" role="img">',
             '<title>Guardrails run on your machine, where the agent has your credentials; boundaries are decided outside the '
             'agent\'s reach</title>', head(t["muted"]),
             f'<rect width="1040" height="400" rx="20" fill="{t["bg"]}" stroke="{t["line"]}"/>',
             f'<rect x="30" y="30" width="440" height="340" rx="14" fill="{t["card"]}" stroke="#F2A33A" stroke-width="2"/>',
             text(56, 70, "Guardrails", t["ink"], 24, 800), text(56, 96, "On your machine, with your credentials", t["muted"], 15),
             text(56, 118, "They prevent accidents; a determined agent can get around them.", t["muted"], 13),
             f'<rect x="570" y="30" width="440" height="340" rx="14" fill="{t["card"]}" stroke="#14808F" stroke-width="2"/>',
             text(596, 70, "Boundaries", t["ink"], 24, 800), text(596, 96, "Outside the agent's reach", t["muted"], 15),
             text(596, 118, "They hold even against a determined agent.", t["muted"], 13)]
    for i, value in enumerate(left):
        y = 140 + i * 44
        parts.append(f'<rect x="56" y="{y}" width="388" height="34" rx="8" fill="#F2A33A" fill-opacity="{t["fill"]}"/>')
        parts.append(text(72, y + 22, value, t["ink"], 15))
    for i, value in enumerate(right):
        y = 140 + i * 44
        parts.append(f'<rect x="596" y="{y}" width="388" height="34" rx="8" fill="#14808F" fill-opacity="{t["fill"]}"/>')
        parts.append(text(612, y + 22, value, t["ink"], 15))
    parts.append(f'<line x1="520" y1="40" x2="520" y2="360" stroke="{t["muted"]}" stroke-opacity="0.6" stroke-width="2" stroke-dasharray="6 6"/>')
    parts.append(f'<rect x="478" y="170" width="84" height="22" fill="{t["bg"]}"/>')
    parts.append(text(520, 186, "pull request", t["muted"], 13, 600, "middle"))
    parts.append(arrow(476, 206, 562, 206, t["muted"]))
    parts.append(f'<g shape-rendering="crispEdges">{rects("on-duty", 908, 286, 4)}</g>')
    parts.append(text(520, 386, "the agent's reach ends here", t["muted"], 12, 400, "middle"))
    parts.append("</svg>\n")
    return "".join(parts)


write("guardrails-boundaries-light.svg", trust(False))
write("guardrails-boundaries-dark.svg", trust(True))


def shell_layout(dark):
    t = theme(dark)
    files = [("CLAUDE.md, NOTES.md", "the team's instructions"), (".claude/", "six hooks, a reviewer agent, skills"),
             (".specify/", "Spec Kit 1.1.0, customised"), ("specs/", "three features, two share 002")]
    units = [("services/orders", ".NET 8 · xUnit v2 on VSTest", "Orders.sln"),
             ("services/billing", ".NET 10 · xUnit v3 on MTP", "Billing.slnx · global.json"),
             ("web/admin", "TypeScript", "package.json")]
    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" width="1040" height="400" viewBox="0 0 1040 400" role="img">',
             '<title>The workspace shell sample: a root repository with the team\'s setup, and three service repositories '
             'beneath it that the shell ignores</title>',
             f'<rect width="1040" height="400" rx="20" fill="{t["bg"]}" stroke="{t["line"]}"/>',
             f'<rect x="30" y="30" width="980" height="340" rx="14" fill="{t["card"]}" stroke="#14808F" stroke-width="2"/>',
             text(56, 68, "Shell repository", t["ink"], 22, 800),
             text(56, 92, "the root, where Claude Code starts", t["muted"], 14)]
    for i, (name, note) in enumerate(files):
        y = 124 + i * 58
        parts.append(f'<rect x="56" y="{y}" width="400" height="46" rx="8" fill="#14808F" fill-opacity="{t["fill"]}"/>')
        parts.append(text(72, y + 20, name, t["ink"], 15, 700, font=MONO))
        parts.append(text(72, y + 38, note, t["muted"], 13))
    parts.append(text(500, 68, "Service repositories beneath it", t["ink"], 18, 700))
    parts.append(text(500, 92, "each its own git repository, ignored by the shell", t["muted"], 14))
    for i, (path, stack, build) in enumerate(units):
        y = 112 + i * 84
        parts.append(f'<rect x="500" y="{y}" width="484" height="72" rx="10" fill="#F2A33A" fill-opacity="{t["fill"]}"/>')
        parts.append(f'<rect x="500" y="{y}" width="6" height="72" rx="3" fill="#F2A33A"/>')
        parts.append(text(522, y + 26, path, t["ink"], 16, 700, font=MONO))
        parts.append(text(522, y + 48, stack, t["muted"], 14))
        parts.append(text(966, y + 26, build, t["muted"], 13, 400, "end", MONO))
        parts.append(text(966, y + 48, ".git", t["teal"], 13, 700, "end", MONO))
    parts.append("</svg>\n")
    return "".join(parts)


write("workspace-shell-light.svg", shell_layout(False))
write("workspace-shell-dark.svg", shell_layout(True))

print("ok", sorted(os.listdir(OUT)))
