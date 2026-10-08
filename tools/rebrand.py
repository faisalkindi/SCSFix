#!/usr/bin/env python
"""Rebrand SCSKiller -> SCSFix across the tracked tree. Re-run it after merging upstream: it is idempotent, and new
upstream files get renamed the same way. Usage: python tools\\rebrand.py [--dry]

Left alone on purpose: upstream's servers and repo (api/dl.scskiller.*, BlueHeisenberg/SCSKiller), the feed-signature
domain string, licence files, third-party notices and the upstream changelog history.

Merging a new upstream release (done for 1.2.3, 8 files conflicted): the fork's names differ from upstream's in every file, so a plain
`git merge upstream/main` conflicts everywhere. Instead rebrand upstream's tree and merge that, with the previous rebranded upstream
tree as the base: (1) `git worktree add --detach W upstream/main`, copy this script to W/tools and run it there, commit; make that
commit a child of the last rebranded-upstream commit in this history (`git commit-tree <tree> -p <it>`; the last is 59766dd,
"rebranded upstream v1.2.3"); (2) on a branch of main `git merge -s ours` that parent, so it counts as merged, then `git merge`
the new commit: its base is the old rebranded tree and only the fork's own changes can conflict. After it, keep the fork's
overrides: `slFirst = false` in ScsFix.cs (upstream won't record a Streamline game on NVIDIA; the fork's hook does), the skipped
Streamline theories in AppTests.Arming.cs, `StreamlineFirst`/RecordOnly/Records11 fields, ScsFix.SelfUpdateDisabled."""
import re, subprocess, sys, pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
DRY = "--dry" in sys.argv

SKIP_FILES = {"LICENSE", "LICENSE-EXCEPTION.txt", "THIRD-PARTY-NOTICES.md", "CHANGELOG.md", "tools/rebrand.py",
              "README.md", "src/SCSFix.Core/App/welcome.json"}   # their text names SCSKiller on purpose (the fork notice, the welcome)
SKIP_PREFIX = ("proxy/third_party/", ".github/assets/")
TEXT_EXT = {".cs", ".cpp", ".h", ".in", ".asm", ".def", ".rc", ".py", ".txt", ".md", ".json", ".xaml", ".csproj", ".props",
            ".slnx", ".ps1", ".yml", ".yaml", ".manifest", ".xml", ".cmake"}

PROTECT = [
    r"https?://[A-Za-z0-9./_?=&%#:-]*",              # every URL (upstream servers, repo, release pages)
    r"[A-Za-z0-9-]*\.?scskiller\.(?:com|io|xyz)\b",    # bare upstream domains (link whitelists)
    r"contact@scskiller\.com",
    r"BlueHeisenberg/SCSKiller",
    r"scskiller-feed-v1",                             # the signature domain string of upstream's feed
    r"Based on SCSKiller by BlueHeisenberg",           # the About page's credit
]
PROT = re.compile("|".join(f"(?:{p})" for p in PROTECT))
SUBS = [("SCSKiller", "SCSFix"), ("SCSKILLER", "SCSFIX"), ("scskiller", "scsfix"), ("ScsKiller", "ScsFix"), ("scsKiller", "scsFix")]


def rebrand(text: str) -> str:
    holes = []
    def hide(m):
        holes.append(m.group(0))
        return f"\x00{len(holes) - 1}\x00"
    text = PROT.sub(hide, text)
    for a, b in SUBS:
        text = text.replace(a, b)
    return re.sub(r"\x00(\d+)\x00", lambda m: holes[int(m.group(1))], text)


def name(path: str) -> str:
    out = path
    for a, b in SUBS:
        out = out.replace(a, b)
    return out


files = subprocess.check_output(["git", "ls-files"], cwd=ROOT, text=True).splitlines()
changed = 0
for f in files:
    if f in SKIP_FILES or f.startswith(SKIP_PREFIX):
        continue
    p = ROOT / f
    if p.suffix.lower() not in TEXT_EXT or not p.is_file():
        continue
    raw = p.read_bytes()
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError:
        continue
    new = rebrand(text)
    if new != text:
        changed += 1
        if not DRY:
            p.write_bytes(new.encode("utf-8"))   # keeps a BOM and the line endings: they are in the text

renamed = 0
for f in sorted(files, key=lambda s: -s.count("/")):   # deepest first so folders move after their files
    if f in SKIP_FILES or f.startswith(SKIP_PREFIX):
        continue
    n = name(f)
    if n != f:
        renamed += 1
        if not DRY:
            (ROOT / n).parent.mkdir(parents=True, exist_ok=True)
            subprocess.check_call(["git", "mv", f, n], cwd=ROOT)
print(f"{'would change' if DRY else 'changed'} {changed} files, {'would rename' if DRY else 'renamed'} {renamed} paths")
