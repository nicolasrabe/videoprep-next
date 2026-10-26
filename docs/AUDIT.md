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
| M5 | Cookies are read from the browser on every yt-dlp call (slow, locks the Chrome DB, causes the DPAPI errors). For scraping this multiplies. | Once per session export a cookie file (`--cookies <file>` together with `--cookies-from-browser`; VERIFY that yt-dlp dumps the jar to the file) and reuse it; refresh when YouTube answers "sign in". Keep the browser fallback chain (`$RetryRegex`). |
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

## Things that are good and must be preserved
Timer/queue-driven non-blocking design (no UI freeze), the cookie fallback chain with "remember the source that worked",
frame-accurate timecode math incl. drop-frame and negative-from-end starts, never overwriting existing files (`Get-UniquePath`),
series numbering that continues after existing files, grouping several clips per downloaded video (download once, cut N),
H.264/AAC verification after each file, i18n key coverage, theme palette.
