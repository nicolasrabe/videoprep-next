# videoprep - architecture (source/videoprep.ps1, v0.1.1)

Single PowerShell file, Windows PowerShell 5.1 compatible (also loads in PS 7), WinForms UI, no external modules.
Launched by `videoprep.bat`: `powershell -NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File videoprep.ps1`.

## File layout (top to bottom)
1. `param(-SetupTools, -ToolsDir, -Need)` + **setup branch**: when called with `-SetupTools` (child process) it downloads yt-dlp.exe,
   FFmpeg (yt-dlp/FFmpeg-Builds `ffmpeg-master-latest-win64-gpl.zip`, extracts only ffmpeg.exe/ffprobe.exe) and Deno into `tools\`, then exits.
2. Assemblies, DPI awareness, **C# via Add-Type** (C# 5 only - no `$""`, `?.`, `nameof`):
   - `YtRunner`: runs a process, redirects stdout/stderr, UTF-8, pushes lines into a `ConcurrentQueue`, background thread sets `Done`/`ExitCode`; `Kill()` uses `taskkill /T /F`.
   - `PasteBox : TextBox`: `Pasting` event on WM_PASTE (0x0302) and `CueText` (EM_SETCUEBANNER 0x1501) for the gray placeholder.
   - `PreviewFetcher`: ThreadPool downloads: `Fetch(id)` = YouTube oEmbed JSON + `i.ytimg.com/vi/ID/mqdefault.jpg`; `FetchThumb(id,url)` = any thumbnail URL. Results in a queue.
3. Constants/state tables (see below), `$I18N` (es/en), themes.
4. Helpers, reused functions, engine, UI functions, window construction (`New-Ctl`, `New-Page`, controls per tab, events, timer, `Shown`).
   `docs/FUNCTION_INDEX.md` lists every function with its line.

## State tables (hashtables, mutated in place - avoids scope problems)
- `$T` tool paths (YtDlp, Ffmpeg, Ffprobe, Deno). `Resolve-Tool` order: `tools\`, script folder, PATH; ffmpeg/deno dirs are prepended to `$env:PATH`.
- `$S` engine: `Mode` idle|download|update|setup, `Step` dl|post, `Runner`, `Jobs`, `Idx`, `Groups`, `Browsers`/`AttemptIdx`, counters, `Folder`, `Cancelled`, post-step fields (`PostSrc/Tmp/Dir/Base/Ext/Note`), `LastJobs`, `LastUpdate`.
- `$P` preview: `Cache[key]`, `Fetcher`, detail-run state (`Runner`, `RunnerId`, `Browsers`, `BIdx`, `DetLine`). **Key** = YouTube id (11 chars) or `h`+12 hex (SHA1 of the URL) for other sites (`Get-LinkKey`). Cache entry: Fetched, Title, Author, Img, Duration, Date, DetState (''|loading|done|noformat|fail), DetRes/DetVL/DetAL, Fps, Other, Url.
- `$Opt` options/appearance: Subs, SubLangs, Thumbs, Archive, Fallback(default true), KeepFull, Crf(14), Limit, Sleep(1), Theme, CustomColor, Lang, Sound, OpenDone, AutoUpdate.
- `$Items` (ArrayList of queue items), `$Draft` (link being prepared: Url, Id, EditId), `$Counter`, `$ClipRows` (clip-row controls), `$RowStatus/$RowFile` (grid status by key), `$UiState` (FmtNow, Busy, Group), `$Pal` (current palette), `$LangMap` (control,text-key pairs), `$LockCtls` (disabled while running).
- Settings JSON: `%APPDATA%\videoprep\settings.json` (folder, naming, prefix, cookie index, cookieFile, option values, theme, customColor, lang, lastUpdate...). Archive: `%APPDATA%\videoprep\downloaded.txt` (yt-dlp `--download-archive`). Old `%APPDATA%\VideoDownloader` is copied once.

## Data model
- **Item** (`New-Item-Object`): Id, Url, VideoId(=key), Site youtube|other, Kind video|audio, Fmt (mp4|movh|movp|webm|mkv|m4a|mp3|wav|flac|opus), Cap (resolution number), NameMode orig|custom|series, Custom, Prefix, Clips (array of @{Start;End} timecode strings), Fps.
- **Job** (built in `Start-Batch`, one per output file = one per clip or one for the whole video): Key `gid|idx`, Gid, Idx, Count, Url, Kind, Fmt, Cap, Label (series/custom name or $null), Start, End, Download (true only for idx 0), Status, Fps, Site.
- **Group** `$S.Groups[gid]`: Full (path of the downloaded full file), Stem, Done, Total, Failed, Keep, PostUsed, HasClips. First job downloads the full video (named `<name>__full.ext` when the item has clips), later jobs reuse it, the full file is deleted after the last job unless KeepFull or a failure.

## Engine flow (timer-driven state machine)
`Start-Batch` (validate, auto-send draft, tools check -> `Start-Setup`, series numbering prompt, build jobs) -> `Begin-Jobs` -> `Start-NextJob` -> `Start-Attempt` (builds yt-dlp args: `Get-DownloadArgs`, subs/thumbs/limit/archive, `-P folder -o name`, cookies via `Get-CookieArgs`) -> `YtRunner`.
`Invoke-Tick` (WinForms timer, 150 ms) drains the queue (`Handle-Line` parses `[download] xx%`), and when the process is done calls `Finish-Attempt`:
update/setup branches; `Step = post` -> `Finish-Post`; cookie/bot-check failure matching `$RetryRegex` -> next cookie source (`Start-Attempt` again); success -> `Complete-Download`.
`Complete-Download` decides if an ffmpeg step is needed (clips; ProRes; H.264 conversion when Fallback or other site and codecs/extension differ) -> `Start-Post` (timing via `Get-SourceTiming`/`Get-AudioTiming`, `Get-CutPlan`, `Get-EncodeArgs`, output `<base>.__tmp<ext>`) -> `Finish-Post` (move to unique final name, verify with ffprobe `Get-MediaInfo`). Otherwise `Finish-Job`. Then `Start-NextJob`; `Complete-Batch` at the end (sound, open folder).
Row statuses are codes (Queued, Downloading, Cutting, OK, WARN, REVIEW, ERROR, Skipped) translated with `rs_<code>`.

## Formats -> yt-dlp/ffmpeg
- Strict H.264 selector `$FmtSafe` = `(bv*[vcodec^=avc1]+ba[acodec^=mp4a])/(bv*[vcodec^=avc1]+ba)/(b[vcodec^=avc1])`, `-S res:CAP,fps`; with Fallback adds `/bv*+ba/b` and merges `mp4/mkv`, else merge+remux mp4 (movh: `--remux-video mov`).
- Other sites: `-f bv*+ba/b -S res:CAP,fps --merge-output-format mp4/mkv`, converted afterwards if needed.
- movp: download `bv*+ba/b` as mkv, ffmpeg `prores_ks -profile:v 3 -vendor apl0 -pix_fmt yuv422p10le`, `pcm_s24le -ar 48000`, `.mov`.
- webm: `bv*[ext=webm]+ba[ext=webm]/bv*+ba/b`, merge `webm/mkv`; clips re-encoded `libvpx-vp9 -crf 24 -b:v 0` + `libopus`. mkv: clips `libx264`+`aac`.
- Audio: `-f ba/b -x --audio-format X --audio-quality 0`; clips re-encoded (aac 256k, mp3 320k, pcm_s16le, flac, opus 160k).
- Clip re-encode video: `libx264 -preset medium -crf <Opt.Crf> -pix_fmt yuv420p`, audio aac 256k, `+faststart`.
- Names: orig = `%(title,id).140s`; custom = text (+` - n` when several clips); series = prefix+number continuing after existing files (prompt). Clips: `__full` suffix for the source.

## Timecode / clip math
`ConvertFrom-TimecodeText`: `HH:MM:SS:FF`, shorter forms, Premiere digit entry (`1523` -> 00:00:15:23), leading `-` = from the end, `;` = drop-frame (29.97/59.94 only). `Get-CutPlan`: real fps from ffprobe, start = first frame, **end = last frame included**, blank end = to the end; times for ffmpeg output-side `-ss`/`-t` use a quarter-frame guard so the exact frames are kept. Frame number must be < fps base.

## Preview
Paste -> `Invoke-SmartPaste` -> `Get-LinksFromText`/`ConvertTo-AnyUrl` -> one link `Accept-Link` (auto-sends previous draft with `Send-Draft`) or many `Add-DefaultItem`. `Request-Preview(url)` -> `Ensure-Meta` (YouTube: oEmbed+thumbnail immediately; other sites: wait for details) -> `Ensure-Details` -> `Start-DetailRun` (yt-dlp `--print "DET|duration|height|fps|vcodec|acodec|date|uploader|thumbnail|title"` with the selected format/cap, cookie chain) -> `Finish-Detail` -> `Show-Preview` (+ `Get-WillText`). Changing format group/cap calls `Reset-Details`. Timeout 45 s in `Invoke-PreviewTick`.

## UI conventions
- Tabs: Descargar, Cola, Ajustes, Registro (owner-drawn headers). Options = modal dialog `Show-OptionsDialog` (global `$Opt`); per-link choices live on the Download tab.
- Texts only through `Tx 'key'` + `Register-Text` + `Apply-Language`; add keys to **both** `$I18N['es']` and `$I18N['en']`. Engine log lines stay English (users paste logs).
- Theme: `Set-ThemeOn` walks controls by type/`Tag` ('muted','link','g','r','o','log'); `Apply-Theme`. Native scrollbars/dialogs stay light.
- Layout is absolute pixels with `AutoScaleMode = Dpi`; form is fixed size 760x640.

## PowerShell pitfalls hit in this project (read before editing)
1. Variables are **case-insensitive**: `$s` == `$S`, `$p` == `$P`, `$t` == `$T`. A local `$s=...` in a function shadows the state table for every function it calls (dynamic scoping). Use other names; event args are `($sender, $ev)`. `tools/build.py` warns about this.
2. **No closures**: do not use `.GetNewClosure()` on handlers (script-scope functions/vars disappear). Store data in `Tag` and read `$sender.Tag`.
3. Save as **UTF-8 with BOM + CRLF** (accents). Keep C# in Add-Type to C# 5.
4. Return arrays with `return ,$array`; wrap callers in `@()`. A function that emits stray output (e.g. unassigned `.Add()`) corrupts return values: use `[void]`.
5. Native args: `ConvertTo-ArgString` quotes for `ProcessStartInfo.Arguments` (no ArgumentList in .NET Framework).
6. yt-dlp output uses `--newline`; ffmpeg runs `-loglevel error -nostats` (its `\r` progress would flood lines).
7. WinForms event handlers run on the UI thread; long work must go through `YtRunner`/`PreviewFetcher` + timer.

## How to patch and build
Function-level patch (Python): read `videoprep.ps1` (utf-8-sig), replace with
`re.sub(r'^function NAME\b.*?^}\n', new_text, s, flags=re.S|re.M)` (all functions close with `}` at column 0), write back as
`'\ufeff' + text.replace('\n','\r\n')` encoded UTF-8. Then `python tools/build.py`. Update `$AppVersion`, `repo/CHANGELOG.md`, READMEs, `source/README.txt`.

## Verification status
Only static checks (brace/quote balance, duplicate functions, variable-shadow scan). **Never executed on Windows by the assistant.** Highest-risk
unverified areas: WM_PASTE capture in a single-line box, cue banner, owner-drawn tabs, theme coverage, ProRes/WebM encodes, `--print` field output for
non-YouTube sites, the exact frame math of clips, bulk behavior of the queue. The user confirmed: tool is useful in daily work; Twitch preview worked after one failed attempt (cause unknown).
