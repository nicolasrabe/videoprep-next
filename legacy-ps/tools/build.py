#!/usr/bin/env python3
"""Lint + package videoprep.  Run from anywhere:  python tools/build.py
Reads source/videoprep.ps1 (edit THAT file), checks it, copies it into repo/,
and writes dist/videoprep-vX.Y.Z.zip (release asset) and dist/videoprep-repo.zip
(files for the GitHub repo root), printing the SHA-256 of the release zip."""
import re, os, sys, zipfile, hashlib, shutil

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC, REPO, DIST = (os.path.join(ROOT, d) for d in ('source', 'repo', 'dist'))
PS = os.path.join(SRC, 'videoprep.ps1')

raw = open(PS, 'rb').read()
errors, warns = [], []
if not raw.startswith(b'\xef\xbb\xbf'):
    errors.append('script must be saved as UTF-8 WITH BOM (Windows PowerShell 5.1 needs it for accents)')
text = raw.decode('utf-8-sig')
if '\r\n' not in text:
    warns.append('script should use CRLF line endings')
lf = text.replace('\r\n', '\n')

for a, b in ('{}', '()', '[]'):
    if lf.count(a) != lf.count(b):
        errors.append('unbalanced %s%s: %d vs %d' % (a, b, lf.count(a), lf.count(b)))

in_here, odd = False, []
for i, l in enumerate(lf.split('\n'), 1):
    if in_here:
        if l.startswith("'@") or l.startswith('"@'):
            in_here = False
        continue
    if re.search(r"@'\s*$", l) or re.search(r'@"\s*$', l):
        in_here = True
        continue
    t = re.sub(r"''", '', l)
    if not t.strip().startswith('#') and t.count("'") % 2 == 1:
        odd.append(i)
if odd:
    warns.append('lines with an odd number of single quotes (check): %s' % odd[:10])

defs = re.findall(r'^function\s+([A-Za-z]+-[A-Za-z-]+)', lf, re.M)
dups = sorted({d for d in defs if defs.count(d) > 1})
if dups:
    errors.append('duplicate function definitions: %s' % dups)

# PowerShell variables are case-insensitive: a local $s/$p/$t shadows the state tables $S/$P/$T
shadow = re.findall(r'^\s*\$(?:p|s|t)\s*=', lf, re.M)
if shadow:
    warns.append('local $p/$s/$t assignments found (they shadow $P/$S/$T in called functions): %d' % len(shadow))

m = re.search(r"\$AppVersion\s*=\s*'([^']+)'", lf)
ver = m.group(1) if m else '0.0.0'
print('version', ver, '| functions', len(defs))
for w in warns:
    print('WARN ', w)
for e in errors:
    print('ERROR', e)
if errors:
    sys.exit(1)

shutil.copyfile(PS, os.path.join(REPO, 'videoprep.ps1'))
shutil.rmtree(DIST, ignore_errors=True)
os.makedirs(DIST)
rel = os.path.join(DIST, 'videoprep-v%s.zip' % ver)
with zipfile.ZipFile(rel, 'w', zipfile.ZIP_DEFLATED) as z:
    for f in ('videoprep.bat', 'videoprep.ps1', 'README.txt'):
        z.write(os.path.join(SRC, f), f)
repo_zip = os.path.join(DIST, 'videoprep-repo.zip')
with zipfile.ZipFile(repo_zip, 'w', zipfile.ZIP_DEFLATED) as z:
    for d, _, fs in os.walk(REPO):
        for f in sorted(fs):
            p = os.path.join(d, f)
            z.write(p, os.path.relpath(p, REPO))
print('SHA-256 of', os.path.basename(rel), hashlib.sha256(open(rel, 'rb').read()).hexdigest())
print('written to', DIST)
