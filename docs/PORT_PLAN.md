# videoprep 0.2.0 - port plan (PowerShell/WinForms -> C#/.NET + Avalonia)

## Why port instead of patching the .ps1
- The goal is a real `videoprep.exe`. Wrapping the script (ps2exe) keeps every problem in AUDIT.md, still compiles C# at
  start and is flagged by antivirus more often.
- The developer may be on macOS: WinForms/PowerShell GUI cannot run there. .NET + Avalonia builds and runs on Windows,
  macOS and Linux, and `dotnet publish -r win-x64` produces the Windows exe from any OS.
- The engine becomes unit-testable and reusable by a headless `--check` mode (needed by Scraping subscriptions).
- Delivers the promised macOS version from the same code (yt-dlp/ffmpeg/deno have macOS builds; cookie paths differ).
- Trade-offs (be honest with the owner): a rewrite is a real amount of work and can reintroduce bugs. Mitigation: the
  legacy script is the behavioral spec, every pure function gets tests ported from it, and 0.1.1 stays available until
  0.2.0 passes the owner's checklist. Self-contained exe is large (about 60-100 MB); that is fine for a GitHub download.
  Do NOT use NativeAOT or aggressive trimming with Avalonia without testing (reflection/bindings).
- If the owner decides against a rewrite, the fallback is `legacy-ps` + the Scraping spec implemented in PowerShell with
  the AUDIT fixes (H1-H4 first). Ask once, in Phase 0, if there is any doubt.

## Target solution layout
```
videoprep/
  README.md  README.en.md  LICENSE(MIT)  CHANGELOG.md  .gitignore
  .github/workflows/ci.yml  release.yml   .github/ISSUE_TEMPLATE/  .github/FUNDING.yml
  src/
    Videoprep.Core/        net LTS class library, NO UI
      Models/              Job, VideoGroup, QueueItem, Subscription, MediaInfo, Settings
      Text/                Timecode, UrlParser (youtube + other sites), SafeName
      Engine/              JobEngine (async, CancellationToken), ProcessRunner, YtDlpArgs, FfmpegArgs, CutPlanner
      Tools/               ToolManager (find/download/update yt-dlp, ffmpeg, deno), CookieStrategy, Probe
      Preview/             PreviewService (oEmbed + yt-dlp metadata, cache)
      Scraping/            SourceLister, FilterSet, SubscriptionStore, ArchiveStore, CheckRunner, RateLimiter
      Storage/             SettingsStore (atomic json), QueueStore, Logging, Paths
      I18n/                es.json, en.json (keys copied from legacy $I18N), Localizer
    Videoprep.App/         Avalonia UI (MVVM, CommunityToolkit.Mvvm). Tabs: Descargar, Cola, Scraping, Ajustes, Registro
    Videoprep.Cli/ (or a --check switch in App)   headless: `videoprep --check` runs subscriptions and exits
  tests/
    Videoprep.Core.Tests/  xUnit; pure unit tests + [Trait("Category","Integration")] tests that need ffmpeg/yt-dlp
  legacy-ps/               untouched reference (can move to a `legacy` branch/tag later)
  docs/                    PORT_PLAN, SCRAPING_SPEC, AUDIT, mockup, RELEASE_CHECKLIST
```
Use the latest installed LTS SDK (`dotnet --version`; pin it in `global.json`). Prefer built-in libs (System.Text.Json,
HttpClient, System.Diagnostics.Process). Allowed packages: Avalonia (+ Avalonia.Controls.DataGrid, Themes.Fluent),
CommunityToolkit.Mvvm, xUnit. Anything else needs a one-line justification.

## Phases (stop and show results after each; wait for "dale")
**Phase 0 - plan (no code).** Read all docs + `legacy-ps/source/videoprep.ps1`. Reply with: the solution
skeleton, anything in the docs you disagree with, anything you cannot verify on this machine, and the order of work.
**Phase 1 - skeleton + CI.** Solution, projects, `global.json`, .gitignore, CI (build+test on windows/macos/ubuntu, publish
win-x64 and osx-arm64 artifacts, SHA-256), hello-window Avalonia app that shows version. `dotnet publish` produces an exe.
**Phase 2 - pure logic with tests (port from legacy).** Timecode parser (examples in the tip text: `1523 -> 00:00:15:23`,
`100 -> 00:00:01:00`, `-00:00:00:15`, `;` drop-frame), frame index/drop-frame math, CutPlanner (same T0/Dur formulas and
error messages), UrlParser (+ the 11-char fix), SafeName, UniquePath, series numbering, YtDlpArgs/FfmpegArgs builders
(snapshot tests of the argument lists per format), Probe JSON parsing with fixture files, Settings load/save/migrate.
**Phase 3 - engine.** ProcessRunner, ToolManager (download/update with progress), CookieStrategy (chain + remembered
source + cookie-file export), JobEngine (download once -> cut N clips -> verify -> move -> cleanup; retry; stop;
group state), `after_move:filepath`, progress template, global error handling. Integration tests with a generated
test video (`ffmpeg -f lavfi -i testsrc2=...`) that check frame counts of cuts.
**Phase 4 - UI parity.** Descargar, Cola (incl. Delete key), Ajustes, Registro, Options dialog, preview panel with explicit
states, themes (6), languages (es/en), drag and drop, tooltips (copy texts from legacy). Resizable window.
**Phase 5 - Scraping.** Implement `SCRAPING_SPEC.md` (Explorar, then Suscripciones, then headless check + Task Scheduler).
**Phase 6 - release.** README es/en, CHANGELOG, RELEASE_CHECKLIST, GitHub Release as pre-release `v0.2.0` with
`videoprep-v0.2.0-win-x64.zip` (+ macOS if built) and SHA-256; test checklist from `legacy-ps/README_FIRST.md` for the owner.

## Parity checklist (must match 0.1.1 unless AUDIT says otherwise)
Formats: MP4 (H.264+AAC), MOV (H.264+AAC remux), MOV (ProRes 422 HQ, PCM 24-bit 48 kHz), WebM (VP9+Opus), MKV; audio M4A,
MP3, WAV, FLAC, Opus. Resolution caps (H.264 YouTube max 1080p; others to 4K). Other sites via best-available then
convert. Clips: HH:MM:SS:FF, several per video, end empty = until end, negative start = from end. Names: original title /
custom / series (continues numbering, asks before continuing). Queue: edit, remove, up/down, retry failed, load .txt,
clear. Pasting a second link sends the first to the queue; several links at once go straight to the queue. Options:
subtitles (SRT, languages), thumbnail, skip already downloaded (archive), H.264 conversion fallback, keep full video,
CRF, speed limit, sleep. Cookies: automatic / each browser / cookies.txt / none. Tools: recheck, update, weekly auto-update.
Sound + open folder when done. Stop button kills the process tree. Close confirms if running.
