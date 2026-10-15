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
        f'<text x="92" y="288" font-family="{FONT}" font-size="22" fill="{body}">Gated, spec-driven delivery for Claude Code agents. .NET first.</text>'
        f'<g shape-rendering="crispEdges">{rects("pass", 850, 70, 15)}</g></svg>\n')


write("banner-dark.svg", banner(True))
write("banner-light.svg", banner(False))


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
print("ok", sorted(os.listdir(OUT)))
