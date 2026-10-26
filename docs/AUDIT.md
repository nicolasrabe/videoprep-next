# Audit of videoprep 0.1.1 (PowerShell) - findings and what to do in 0.2

Source read in full: `legacy-ps/source/videoprep.ps1` (3,120 lines), build script, docs. Nothing here was executed on
Windows; findings come from reading the code. Severity: **H** = can lose data or block the roadmap, **M** = bug/risk, **L** = polish.
"Port" = how the C# rewrite must behave.

## High
| # | Finding (where) | Port / fix |
|---|---|---|
| H1 | Engine is welded to UI controls: `Start-Attempt`, `Handle-Line`, `Finish-*`, `Get-BrowserList` read/write `$bar`, `$lblStatus`, `$cmbCookie`, `$txtFolder`, `$txtCookieFile` directly. A headless run (needed for subscriptions with the app closed) and unit tests are impossible. | `Videoprep.Core` has zero UI deps. Settings object is passed in; progress via events. A `--check` CLI mode reuses it. |
| H2 | Output file is found by "newest media file written since job start" (`Find-ResultFile`). Wrong file if two things write, if a file was skipped, or in busy folders; the cause of the "SKIPPED / no new file" guesswork. | `--print after_move:filepath` (one parsed line per output). Fallback only if the line is missing. |
| H3 | `Finish-Post` deletes the full source (`Remove-Item $g.Full`) BEFORE moving the temp output to its final name. If the move fails, the source is gone; the temp is never probed first. | Order: ffmpeg ok -> ffprobe the temp (streams exist, duration within tolerance of the plan) -> move to unique final name -> only then delete the source. |
| H4 | Errors in most WinForms event handlers are not caught (`$btnStart.Add_Click({ Start-Batch })`, grid, theme...). Many other places use `catch { }` and hide the cause. Unhandled exceptions give the raw .NET dialog. | One global handler (UI thread + unobserved task exceptions) that logs full stack to the Log tab and `%APPDATA%\videoprep\logs\`, plus a friendly message. No empty catches: log at least at debug level. |
| H5 | Nothing was ever executed on Windows; `tools/build.py` only counts braces/quotes. | Real compiler + unit tests + CI on windows-latest, macOS and Linux. Integration tests (opt-in) run real ffmpeg on a generated test clip and check frame counts. |
| H6 | Subscriptions/scraping need persistent state, a per-subscription archive, a non-UI runner and rate-limit handling. None exists; queue itself is not persisted. | See `SCRAPING_SPEC.md`. Persist the queue too (`queue.json`) so a crash or close does not lose it. |

## Medium
| # | Finding | Port / fix |
|---|---|---|
| M1 | State tables named `$S`, `$P`, `$T`; PowerShell variables are case-insensitive, so locals `$s`, `$p`, `$t` shadow them (e.g. `Get-CutPlan` uses `$s`/`$e`; `Set-ThemeOn($ctl,$p)`; `Resolve-Tool` loops with `$p`). Works today only because those functions do not touch the tables. build.py only warns. | Disappears in C# (typed classes: `EngineState`, `PreviewCache`, `ToolPaths`). |
| M2 | ffprobe is run 2-3 times per file and the `key=value` parsing is copy-pasted three times (`Get-MediaInfo`, `Get-SourceTiming`, `Get-AudioTiming`). | One `Probe` service: `ffprobe -v error -show_streams -show_format -of json`, parsed once into a typed `MediaInfo`. |
| M3 | Clip cut uses output seeking (`-ss` after `-i`): decodes from the start of the file, slow for clips late in long videos. | Fast seek before `-i` to a few seconds earlier + exact `-ss` after `-i` (hybrid). Keep the frame-accurate math (`T0 = (S-0.25)*fd`, `Dur = ((E+0.75)*fd)-T0`). Add an integration test that counts output frames. |
| M4 | Frame rate comes from `r_frame_rate`; wrong for variable-frame-rate sources. Docs already warn. | Read `avg_frame_rate` and `r_frame_rate`; if they differ by >1% flag "variable frame rate" and warn or force `-vsync cfr` conversion. |
| M5 | Cookies are read from the browser on every yt-dlp call (slow, locks the Chrome DB, causes the DPAPI errors). For scraping this multiplies. | Once per session export a cookie file (`--cookies <file>` together with `--cookies-from-browser`; VERIFIED that `--cookies FILE` dumps the jar; the combination with a browser is not verified yet, see docs/VERIFY.md) and reuse it; refresh when YouTube answers "sign in". Keep the browser fallback chain (`$RetryRegex`). |
| M6 | Progress is scraped from the human text (`[download]  45.2% ...`). Breaks if yt-dlp reformats. | `--progress-template "download:VP|%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s"` and parse that. |
| M7 | Absolute-pixel layout in a fixed 760x640 window (`New-Ctl x y w h`). A table-heavy Scraping tab cannot fit. | Resizable window, grid/dock layouts, min size 900x640. |
| M8 | Settings: hand-written key-by-key load and save, written non-atomically; unknown/new keys are easy to forget. | Typed `Settings` class with defaults, `System.Text.Json`, write to temp then replace, version field + migration (also migrate `%APPDATA%\VideoDownloader` once, as today). |
| M9 | Long paths: names use `%(title,id).140s` and `__full`, `.__tmp` suffixes in the user folder; paths over 260 chars fail on some setups. | `--windows-filenames`, shorter title cap (about 100), check total length and shorten. |
| M10 | `ConvertTo-YoutubeUrl` accepts any 11-character token as a video ID (e.g. the word "Hello_World"). | Accept bare 11-char IDs only if the token has no spaces AND the user pasted just that; otherwise treat as text. Cover with tests. |
| M11 | Preview: a failed or slow yt-dlp metadata run can stay on "Cargando..." (known, backlog 0.1.2 #3). Timeout exists (45 s) but the UI state/log is not written, no retry without cookies. | Preview service with explicit states (loading / ok / failed + reason), yt-dlp stderr to Log, one retry without cookies, queue grid title kept in sync. |
| M12 | `Start-Retry` re-runs failed jobs but creates a `$seen` variable it never uses; group bookkeeping (`Done/Total/Failed/Keep`) is spread over five functions. | Model a `VideoGroup` object with methods; unit-test the state transitions. |

## Low / polish
- L1 Header comment says "keep this file ASCII-only" but the file is UTF-8 with BOM and has accents; contradictory. (Moot in C#.)
- L2 `Add-Type` compiles C# on every start (about 1-2 s, antivirus heuristics) and the launcher uses `-ExecutionPolicy Bypass -WindowStyle Hidden`; both raise SmartScreen/AV suspicion. A signed .exe is far better (signing is a later decision).
- L3 `source/videoprep.ps1` and `repo/videoprep.ps1` are copies; build.py syncs them. Single source of truth in the new repo.
- L4 Non-approved verbs and odd names (`New-Item-Object`, `Mix-Color`, `Handle-Line`) - cosmetic.
- L5 `Write-Log` trims by text length; fine, but log lines are English while the UI is Spanish (kept on purpose for bug reports). Keep: logs English, UI localized.
- L6 `FormClosing` only handles the running download; setup/update children may linger. Kill the whole process tree on exit.
- L7 Window is not resizable and has no keyboard shortcuts besides Ctrl+A/Enter. Add Delete in the queue (backlog), Ctrl+V, F5.

## Found in Phase 0 (2026-10-25, reading the .ps1 again)
| # | Finding | Port / fix |
|---|---|---|
| X1 | `Get-SafeName` only strips `\/:*?"<>|%`: control characters, trailing dots/spaces and reserved names (`CON`, `NUL`, `COM1`...) still break Windows file names. | `SafeName` strips control chars, trims trailing dots/spaces, renames reserved names; unit tests. |
| X2 | Preview with an **audio** format still asks yt-dlp for the H.264 selector (`Start-DetailAttempt`), so it can wrongly say "No H.264 version". | Audio preview uses `ba/b`. |
| X3 | The cookie source that worked (`$Pref.Browser`) lives only in memory; it is lost on restart. | Persist it in settings. |
| X4 | Audio clips use the fps from the preview; if the preview did not finish, 30 fps is assumed silently (frames 30-59 rejected for 60 fps sources). | Log the fps used; if unknown, say so in the clip error/note. |
| X5 | Name example shows `Title - 1, Title - 2` even when there are no clips (cosmetic). | Example depends on the clip count. |
| X6 | Found while verifying (docs/VERIFY.md): when the `__full` file already exists, yt-dlp reuses it and still prints its path, so the legacy cleanup could delete a file the user already had. | Engine records whether the file existed before the job and never deletes a pre-existing file. |
| X7 | `tip_tc` (es) said "drop frame", an anglicism. | Spanish text now says only "descarte de fotogramas". Checked by a test that scans the Spanish texts. |

## Phase 0 decisions (approved 2026-10-25)
- Tools live in `%LOCALAPPDATA%\videoprep\tools` (also looked up beside the exe and in PATH). Logs and caches in `%LOCALAPPDATA%\videoprep`.
- M5: the exported cookie file is kept in the user's profile folder, deleted when videoprep closes, and Settings explains it.
- M3: clip cut = input-side `-ss` with the same T0/Dur math plus `-frames:v N` as a guard; the frame-count integration test decides (fall back to hybrid seek if not exact).
- `videoprep --check` exit code 3 = another run holds the lock.
- macOS build after 0.2.0 (no macOS ffmpeg from yt-dlp/FFmpeg-Builds; needs another source + Gatekeeper quarantine handling).
- Release exe uses `EnableCompressionInSingleFile`; no trimming/NativeAOT.

## Status after Phase 3 (2026-10-26)
Done in `Videoprep.Core` with tests: H1 (engine without UI), H2 (`after_move:filepath`), H3 (ffprobe check -> move -> only
then delete), H4 (log + crash file for unhandled errors; friendly message box comes with the phase 4 screens), H6 queue part
(`queue.json`), M2, M3 (verified frame-exact), M4 (VFR warning in the log), M5 (session cookie copy; **not tried with real
browser cookies**), M6, M12 (`VideoGroup`), L6 (kill the process tree), X3 (preferred browser reported by the engine),
X4 (fps guess noted in the log), X6. Tool download/update (`ToolManager`) tested with a fake web server only.

## Status after Phase 4 (2026-10-26)
Also done, with view-model tests and screenshots of every tab in every theme and both languages: H4 (friendly message for UI
errors), H6 queue in the UI (restored on start), M7 (resizable, min 900x640, checked at the minimum size), M11 (preview
states, retry without cookies, time limit, reason shown), L7 (Delete key; paste and Enter in the link box), X2, X3 (saved),
X5. Spanish texts: "MISSING"/"found" in the tools box and "yt-dlp is not installed" in the preview were English in 0.1.1
and are now translated (the log stays English). Not done: Windows title bar stays light in dark themes (Avalonia sets the
window content only); F5 shortcut (no clear use yet).

## Things that are good and must be preserved
Timer/queue-driven non-blocking design (no UI freeze), the cookie fallback chain with "remember the source that worked",
frame-accurate timecode math incl. drop-frame and negative-from-end starts, never overwriting existing files (`Get-UniquePath`),
series numbering that continues after existing files, grouping several clips per downloaded video (download once, cut N),
H.264/AAC verification after each file, i18n key coverage, theme palette.
