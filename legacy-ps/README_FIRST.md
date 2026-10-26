# videoprep - handoff package

Everything a developer needs to continue **videoprep**, a free Windows
tool (PowerShell + WinForms) that prepares videos for editing: it wraps yt-dlp, FFmpeg and Deno.
Snapshot: **version 0.1.1** (built, partly tested by the user). The user's working language is
**Spanish (Rioplatense, Argentina)**; documents here are in English.

## What is in this package
| Path | What it is |
|---|---|
| `source/videoprep.ps1` | THE program (single file, ~3,100 lines). Edit this file. UTF-8 **with BOM**, CRLF. |
| `source/videoprep.bat` | Launcher (hidden console, `-ExecutionPolicy Bypass -STA`). |
| `source/README.txt` | Short bilingual readme shipped inside the release zip. |
| `repo/` | Files for the GitHub repo root (`nicolasrabe/videoprep`): README es/en, LICENSE (MIT), CHANGELOG, `.github/`, `docs/RELEASE_CHECKLIST.md`, script copy. |
| `tools/build.py` | Lints the script and builds `dist/videoprep-vX.Y.Z.zip` (release asset) + `dist/videoprep-repo.zip`, prints the SHA-256. |
| `docs/PROJECT.md` | User, workflow, vision, decisions, distribution/legal notes. **Read first.** |
| `docs/ARCHITECTURE.md` | How the code works, conventions, PowerShell pitfalls, how to patch/test. |
| `docs/BACKLOG.md` | Confirmed 0.1.2 list, later ideas, scratched ideas, known risks. |
| `docs/FUNCTION_INDEX.md` | All 110 functions with line numbers. |

## How to resume (3 steps)
1. Read `docs/PROJECT.md`, then `docs/ARCHITECTURE.md`, then `docs/BACKLOG.md`.
2. Edit `source/videoprep.ps1` with function-level patches (recipe in ARCHITECTURE.md), then run
   `python tools/build.py`. Keep the version in `$AppVersion`, `repo/CHANGELOG.md` and READMEs in sync.
3. Give the user the two zips plus short GitHub steps (replace repo files, release tag `vX.Y.Z`,
   pre-release, SHA-256 recalculated on their PC - the hash changes with every zip build).

## Working agreements with the user (important)
- Reply in **Spanish (voseo)** unless the user writes English. Short, direct, no fluff. They said
  "no digas boludeces / no me cocines rápido": be accurate, say what is uncertain, do not oversell.
- **Do not build until they say "dale, compilá"** (or "buildeá"). Before big changes show a cheap mockup
  or plan first; they are on a free plan and care about tokens.
- The sandbox cannot run PowerShell/WinForms: nothing was ever executed on Windows by the assistant.
  Static checks only. Be honest about that and ask for the **Log tab output** when something fails.
- Anglicism-free Spanish in the UI ("enlace", "cola", "fotogramas", "código de tiempo").
- One question per reply at most. Project name is always lowercase: **videoprep**.
- Memory of the old chat is not available to a new instance; this package is the memory.

## Test checklist for any new build
First run (tool download) -> paste YouTube link / short link / bare ID -> preview -> clips with timecode
(`-00:00:00:15` = last 15 frames) -> MP4 + MOV(ProRes) + audio -> paste a 2nd link (first goes to queue) ->
queue edit/remove/retry -> language and theme switch -> Twitch/X/Instagram link -> Stop button -> Log tab
"Test setup".

