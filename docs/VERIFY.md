# VERIFY results

Every flag the docs marked "VERIFY" (plus the ones the engine depends on), run for real on Windows 10.
Versions: **yt-dlp 2026.10.21**, **ffmpeg/ffprobe N-128097-g46f23be8b1-20261025** (yt-dlp/FFmpeg-Builds win64 gpl),
**deno 2.9.8**. Date: 2026-10-26. No browser cookies were used. Re-check when yt-dlp is updated.

| Flag / behavior | Result | Consequence for the code |
|---|---|---|
| `--js-runtimes deno:PATH` | Works with an **absolute** path to `deno.exe` or to its folder. A relative path (`.tools/deno.exe`) is silently ignored and yt-dlp warns "No supported JavaScript runtime could be found". | Always pass the absolute path. Treat that warning as a tools problem in the log. |
| `--print after_move:filepath` | Prints one line with the absolute final path (after merge/remux). | H2 fix confirmed. |
| ...when the ID is already in `--download-archive` | Prints **nothing**, exit code 0. | Engine reports "Skipped (already downloaded)". |
| ...when the file already exists (no archive, `--no-overwrites`) | Still prints the existing path, exit 0. | **Never delete a `__full` file that existed before the job started** (check before running). |
| `--print` + progress | `--print` implies quiet: **no progress lines** unless `--progress` is added too. | Always add `--progress --newline`. |
| `--progress-template "download:VP\|%(progress._percent_str)s\|%(progress._speed_str)s\|%(progress._eta_str)s"` | Lines like `VP\| 41.5%\|   7.16MiB/s\|00:00`; fields have leading spaces; last line can be `VP\|100.0%\|Unknown B/s\|NA`. | Parser trims fields and accepts `NA` / `Unknown`. |
| `--download-archive` file format | One line per video: `youtube jNQXAC9IVRw`. | Archive store reads/writes `<extractor> <id>`. |
| `--flat-playlist -J <channel>/videos` | Fast. Entries have `id`, `title`, `duration` (seconds), `view_count`, `url`, `ie_key`; `upload_date`, `timestamp`, `live_status`, `availability` are **null**. | Fixture saved: `tests/Videoprep.Core.Tests/Fixtures/youtube-flat-channel-approx.json`. |
| `--extractor-args "youtubetab:approximate_date"` | Exists. Fills `timestamp` (approximate, day precision); `upload_date` stays null. | Show date from `timestamp` with "~". |
| `--playlist-end N` | Still accepted but no longer in `--help` (old alias). | Use `-I 1:N` (`--playlist-items`) instead. |
| `--match-filters "duration>=100 & !is_live & availability!=subscriber_only"` | Works in flat mode; entries with a missing field (`is_live`, `availability` = null) **pass** the `!`/`!=` tests. | Client-side filters stay the main filter; yt-dlp filters are only a second guard. |
| `--break-on-existing` with `--download-archive` | Works in flat and full mode; stops at the first archived entry with **exit code 101**. | Exit 101 = "stopped early on purpose", not an error. |
| `--dateafter YYYYMMDD` (single video, full extraction) | Video outside the range is skipped silently, exit 0. | OK as the download-time safety net. |
| `--cookies FILE` | yt-dlp writes the cookie jar to FILE at the end (Netscape format), even without a browser. | M5 export works. **Not verified:** the combination with `--cookies-from-browser` (needs Nico's browser; ask first). |
| `--windows-filenames`, `--trim-filenames N`, `-w/--no-overwrites`, `--remux-video mov/mp4`, `--sleep-requests`, `--sleep-interval` + `--max-sleep-interval`, `--lazy-playlist`, `--break-match-filters`, `--max-downloads`, `--force-write-archive` | Present in `--help` of this version (not all exercised in a run). | Usable. |
| Preview line `--print "DET\|%(duration_string)s\|..."` with the strict H.264 selector | `DET\|19\|240\|15\|avc1.4D400C\|mp4a.40.2\|20050424\|jawed\|https://i.ytimg.com/...\|Me at the zoo`. Note `duration_string` is `19` (no `0:`) for short videos. | Preview parser must not assume `M:SS`. |

## Phase 3 (2026-10-26, same ffmpeg)

| What | Result |
|---|---|
| AUDIT M3: input-side `-ss T0` + `-t Dur` + `-frames:v N` (re-encode) | **Frame-exact.** `IntegrationTests.cs` cuts generated videos whose every frame has its own brightness, then decodes every output frame: the count, the first frame and the last frame match the plan at 23.976, 25, 29.97 and 59.94 fps, from the start, mid-GOP, the last 15 frames, a single frame and drop-frame after the first minute, for MP4, MOV H.264, ProRes, WebM and MKV. Hybrid seek is not needed. |
| Audio clips (m4a, mp3, wav, flac, opus) | Length within 2 frames / 0.1 s of the plan. |
| Engine end to end with the fake yt-dlp + real ffmpeg | Downloads once, cuts 3 exact clips, verifies each with ffprobe, deletes the full file only after the last verified clip; never deletes a pre-existing full file; never overwrites an existing output. |
| Kill the process tree on Stop | yt-dlp and a child process it started both end (fake yt-dlp test). |
| Real YouTube download + cut with `--demo` (run by Nico on Windows 10, 2026-10-26) | "Big Buck Bunny 60fps 4K" (aqz-KE-bpKQ, CC BY), no cookies, `--resolucion 480p`: YouTube served H.264 854x480 at **30 fps** (60 fps H.264 only from 720p). `00:00:10:00`-`00:00:12:29` gave **90 frames** (frames 300-389), confirmed by the demo's ffprobe check and in Premiere. A first try with `00:00:12:59` was correctly refused (frames must be 0-29) and the download was kept. |

## Phase 5 - Scraping listings (2026-10-26, yt-dlp 2026.10.21, no cookies)

Real runs against the Blender Studio channel (`https://www.youtube.com/@BlenderStudio`), listings only, 2-3 entries per
tab, `--sleep-requests 1`. Saved as fixtures in `tests/Videoprep.Core.Tests/Fixtures/listing/`.

| Flag / behavior | Result | Consequence for the code |
|---|---|---|
| `--flat-playlist -j <channel>/<tab>` | One JSON object per line, per entry, as the entries are found. | Results stream into the table (no need to wait for `-J`). |
| `/videos` tab | `id`, `title`, `duration`, `view_count`, `url`; with `youtubetab:approximate_date` also `timestamp` **and `upload_date`** (in `-j` mode; `-J` left `upload_date` out). Both are approximate (day precision). | Show the date with "~"; confirm exact dates only when a date filter needs them. |
| `/shorts` tab | `id`, `title`, `view_count`, `url` (`/shorts/ID`); **no `duration`, no date** even with `approximate_date`. | Date/duration filters on Shorts need the exact metadata step. |
| `/streams` tab | Like videos, plus `live_status: "was_live"` for past streams. Current/upcoming streams would be `is_live` / `is_upcoming`. | "Skip live" skips `is_live` and `is_upcoming` only; past streams are normal videos. |
| `/playlists` tab | Entries are playlists: `ie_key: "YoutubeTab"`, `url: https://www.youtube.com/playlist?list=...`, no counts. | Shown as list rows; "Ver videos de la lista" lists one playlist. |
| `https://www.youtube.com/playlist?list=ID` | Its videos, with `duration`, approximate `timestamp`/`upload_date`, `playlist_id`, `playlist_title`. | Same parser as channel tabs. |
| Exact metadata: `--skip-download --print "EX\|%(id)s\|%(upload_date)s\|%(duration)s\|%(live_status)s\|%(availability)s" URL1 URL2...` | One line per video (`EX\|Qqmj29bOcLA\|20261022\|43\|not_live\|public`), exit 0. Each video is a full extraction (several requests). | Only for entries that pass the other filters and miss a needed field; batches of 10, cap 100, `--sleep-requests 1`. |
| Missing tab (`@jawed/shorts`) | `ERROR: [youtube:tab] @jawed: This channel does not have a shorts tab`, exit 1. | Reported as "this channel has no Shorts tab", not as a failure. |
| Archive line format | `youtube <id>` (ie_key in lower case + id), confirmed in phase 2. | "Already downloaded" = `ie_key.lower() + " " + id` is in the archive. |
| `--match-filters` | Missing fields pass `!`/`!=` tests (phase 2). | Filters are applied by videoprep on the listing; yt-dlp filters only as a second guard when downloading. |
| Profiles of other sites (2026-10-26, `--flat-playlist -j -I 1:3`) | Twitch `/<user>/videos`, Kick `/<user>/videos`, Vimeo `/<user>`: entries with id, title, url. Instagram `/<user>/`: `--list-extractors` says `instagram:user (CURRENTLY BROKEN)`; run gives `ERROR: [instagram:user] ...: Unable to extract data`. X `x.com/<user>`: `ERROR: Unsupported URL` (no profile extractor; only single posts). TikTok `/@user`: `ERROR: [tiktok:user] ...: Unable to extract secondary user ID`. | Explorar recognizes Instagram/X/TikTok profile links and explains instead of failing (copy post links into Download). Re-check after yt-dlp updates. |

Seen only on the CI (Ubuntu's own ffmpeg, not the yt-dlp FFmpeg build; see "WebM audio on Linux" below): `ffprobe -count_frames` on a WebM clip printed
"Error parsing Opus packet header" and sometimes no count. The tests now count frames by decoding only the video stream with ffmpeg, and check separately that the audio decodes without errors. Not seen
with the Windows build videoprep downloads; to look at again if a user reports broken Opus audio in WebM clips.

Still to verify later: Chrome/Edge/Firefox cookie reading on Nico's PC (with permission), 429 / "not a bot" messages (only seen when they happen).

Final path with symbols in the title (2026-10-26, yt-dlp 2026.10.21, Windows, PYTHONUTF8=1): for `685eur9lMGc`
("OVERGROWN Project: What will you get?") `--windows-filenames` names the file `OVERGROWN Project： What will you get？.mp4`
(full-width colon and question mark), but `--print after_move:filepath` on the console prints
`...OVERGROWN Project What will you get.mp4` (symbols missing), so videoprep did not find the file and logged "SKIPPED"
(Nico's report). `--print-to-file after_move:filepath FILE` writes the exact path in UTF-8. The engine now reads that
file first; checked end to end with `videoprep --demo` on that video (file found, clip cut and verified).

Subscriptions on Nico's PC (2026-10-26, Windows 10): a real check of @BlenderStudio downloaded the expected videos and a second check downloaded nothing (after the max-count fix). The scheduled task was created from the app's "Configurar..." button (`schtasks /Create /TN videoprep-check /TR "<exe> --check" /SC HOURLY /MO 6 /IT /F`, built with ArgumentList; HOURLY accepts /MO 1-23, so 24 h uses DAILY /MO 1), started with `schtasks /Run /TN videoprep-check`, and it ran `videoprep.exe --check`: logs/check-20261026.log shows the listing, "0 new" and "Exit code 0". Then removed.

## 0.3 round 1 (2026-10-28, yt-dlp 2026.10.21, Windows 10)

| What | Result | Used for |
|---|---|---|
| Queue row card: `--ignore-config --no-playlist --no-warnings --skip-download --ignore-no-formats-error --js-runtimes deno:<abs> --print "INFO\|%(.{title,channel,uploader,upload_date,view_count,duration,duration_string,description,thumbnail,webpage_url})j" <url>` on `aqz-KE-bpKQ` (Big Buck Bunny, CC BY) | One line `INFO\|{"title": "Big Buck Bunny 60fps 4K - Official Blender Foundation Short Film", "channel": "Blender", "uploader": "Blender", "upload_date": "20141110", "view_count": 23481387, "duration": 635, "duration_string": "10:35", "description": "...", "thumbnail": "https://i.ytimg.com/vi_webp/.../maxresdefault.webp", ...}`, exit 0. A `WARNING: Unknown codec iamf...` line goes to stderr even with `--no-warnings`; it does not matter. | `VideoInfoService` (F3). The YouTube picture is taken from `i.ytimg.com/vi/<id>/hqdefault.jpg` (JPEG) instead of the WebP given here. Checked end to end by `NetworkTests` (opt-in, `VIDEOPREP_NETWORK_TESTS=1`): one yt-dlp run, card and picture in about 4 s. |
| Background titles: `https://www.youtube.com/oembed?format=json&url=<watch url>` | Title and author, no cookies, no yt-dlp. | Titles of YouTube links loaded from a file (one at a time, 1.5 s apart, at most 100 per file). Checked by `NetworkTests`. |

## 0.3 round 2 (2026-10-29, yt-dlp 2026.10.21, Windows 10)

| What | Result | Used for |
|---|---|---|
| A second `--print-to-file` in the same download run: `--print-to-file "after_move:%(.{title,channel,uploader,webpage_url})j" meta.txt` next to `--print-to-file after_move:filepath paths.txt`, on `aqz-KE-bpKQ` (Big Buck Bunny, CC BY), `-f worstaudio` | `meta.txt` has one line `{"title": "Big Buck Bunny 60fps 4K - Official Blender Foundation Short Film", "channel": "Blender", "uploader": "Blender", "webpage_url": "https://www.youtube.com/watch?v=aqz-KE-bpKQ"}`; `paths.txt` the exact path; the console shows only the path (the JSON goes to the file only). No extra request. | Title and channel of the sources list (F7) without asking the site again. |

### Mini window on the real Windows desktop (2026-10-29, Windows 10, `tests/desktop/mini-focus-check.ps1`)
Run with the published `videoprep.exe`, its own data folder (`VIDEOPREP_HOME`), cookies off, a Notepad as "the program where the user types", and the free-licence Big Buck Bunny downloading at 1 MB/s. **All 38 checks pass** (the last run, on the exe built from the final commit). What it showed and what was fixed because of it:
- Opening the mini does not take the keyboard focus; 40 keys typed in Notepad over 14 s of progress updates all arrived and the focus never left Notepad (`ShowActivated=false` + the "no activate" window style, set through `Platform`).
- The mini has no taskbar button, stays on top, the big window is minimized (one taskbar button); it can be dragged from anywhere and remembers where it was (`miniX/miniY` in settings.json).
- Right-click menu: it opens without taking the focus. A window that is never activated receives **no Esc key and no "click outside" signal from Windows**, so the mini watches the keyboard and mouse itself while its menu is open (`Platform.IsKeyDown`, 30 ms): Esc closes it, a click outside closes it, a click on an item runs the item. First run found three bugs, all fixed: a very short Esc press was missed; "click outside" fired before a click on an item arrived (the menu closed before the item ran); the menu check mark did not save (`MenuItem.IsChecked` is one-way unless `Mode=TwoWay`). After the menu closes the focus is still in Notepad.
- "Siempre encima" turns the Windows topmost flag off and on live (also saved); "Cerrar videoprep" asks "Todavía hay algo en curso..." while a download runs and "No" keeps everything; "Cerrar modo mini" brings the big window back.
- **Size:** Windows will not make this frameless window lower than 39 px (its minimum tracking height). `Height=28`, `MinHeight=28`, `ClientSize` after opening and `SetWindowPos` were all tried; the mini measures **300 x 39 px** at 100 % scale.
- Not touched on purpose: the real clipboard (setting it would overwrite the user's clipboard).

## WebM audio on Linux: "Error parsing Opus packet header" (2026-10-29, CI run of e13320d)
The CI failed on the WebM cut once the audio of every cut had to decode with no message (`AssertAudioDecodes`, strict: any line at
`-v error` fails). To find out whether our cut is damaged or the reader is picky, the CI made the same cut (the "webm" case of
`Other_video_formats_are_frame_exact`, same arguments) with two ffmpeg builds and read each file with each build:

| ffmpeg | version |
|---|---|
| "apt" = what `ubuntu-latest` installs | 8.0.1-3ubuntu2 (not 6.1.1: the runner image moved on) |
| "videoprep" = the Linux twin of the build videoprep downloads (yt-dlp/FFmpeg-Builds, master-latest-linux64-gpl) | N-128170-g0ea6606418-20261028 |

| file made by | read by apt 8.0.1 | read by videoprep's build |
|---|---|---|
| apt 8.0.1 (libopus, Lavc62.11) | 1 message: `[opus] Error parsing Opus packet header.` | none |
| videoprep's build (libopus, Lavc63.25) | 1 message: the same | none |

- The message also appears when apt's ffmpeg only **reads** the file without decoding (`-c copy`), so it comes from its Opus/Matroska
  packet parser, not from the decoder, and it comes with **both** files; the videoprep build reads **both** with no message at all.
  Same file, different reader: the file is not what changes the result.
- The audio streams look normal in both: Opus, 48 kHz mono, `initial_padding=312`, 45 packets, the first and the last packet about
  twice as big as the others (924 and 953 bytes against ~520, expected at the start and the end of an Opus stream). apt's ffprobe
  puts the audio start at -7 ms (`start_pts=-7`), the other at 0.
- The whole integration suite (all cuts, the engine with the fake yt-dlp) was also run with the videoprep build in the same CI run:
  all passed. On Windows the same suite passes with the Windows build of the same family (local run, N-128097).
- **Decision:** the `integration` job now downloads that build (cached; the key `ffmpeg-ytdlp-linux64-v1` pins it, change the number to
  take a newer one) and sets `VIDEOPREP_FFMPEG_DIR`; the Ubuntu package is no longer installed. The strict audio check, the frame count
  and every other test are unchanged. Not proven: what exactly in apt 8.0.1's parser rejects the packet (it was not looked into further);
  if a user ever reports damaged Opus audio in WebM clips this table is the first place to look.

## 0.3 round 3, F8 "Velocidad constante" (2026-10-29, ffmpeg N-128097 on Windows, N-128170 in the CI)
Test clip (made in code, `Ffmpeg.MakeVfrSource`): 6 s at 60 fps with the frame number in the brightness, frames dropped in three stretches
(~50, ~30 and ~10 fps) and a tone of exactly the same length, muxed apart. ffprobe says `r_frame_rate=60/1`, `avg_frame_rate=5400/173`
(31.2 fps), intervals of 16.7, 33.3 and 100 ms: really irregular, not just two numbers that differ.

| Question | What the real ffmpeg did | What the code does |
|---|---|---|
| `-fps_mode cfr -r 30` on the whole video | 178 frames, 5.933 s while the audio lasts 5.767 s: the video came out 5 frames longer than the audio (the last frame of a variable video lasts as long as the gap before it) | Whole videos get `-t <length of the original>`: 174 frames, 5.800 s (one frame more) |
| `-fps_mode cfr` + `-shortest` | 172 frames (one less) | not used |
| `fps` filter with `eof_action=round` | 178 frames, same problem | not used |
| Clip frames 30-59 in the same pass (`-ss 0.991667 -t 1 -frames:v 30 -fps_mode cfr -r 30`) | exactly 30 frames, video and audio 1.000 s | same arguments |
| NTSC `-r 30000/1001` | `r_frame_rate = avg_frame_rate = 30000/1001`; with `-video_track_timescale 30000` the track clock is `1/30000` (exact) | the timescale is added for MP4/MOV |
| WebM / ProRes MOV | WebM `30/1`, no duration in the container (packets are counted); ProRes `30/1`, PCM audio | WebM checks use packet times with a 1 ms rounding allowance |
| A constant 30 fps video through the same command | unchanged (173 frames) | never run: constant videos are left untouched (same bytes) |

Checks before anything is replaced (`CfrVerifier`, plus the usual `OutputVerifier`): frames (exact for a clip; the expected one +-1 for a whole video), length
(+-1 frame), audio start (+-1 frame or 30 ms of where it started in the ORIGINAL), audio length (+-1 frame or 60 ms of the original's, or of what the clip needs:
AAC adds 21 ms of its own at the start, PCM and Opus add other amounts; the first real run of ProRes showed 36 ms of that), and the result must be constant now
(the intervals between frames all equal). Audio is compared with the original's audio, not with an ideal: a file that arrived 50 ms out of step stays so and is not
blamed on the conversion. A failure deletes the temporary file, leaves the original as it was and sends the row to "Revisar" with the reason. Whole videos
that stay in the same container (an MP4 made constant is an MP4) take the clean name only after the original is deleted, never before the check.
The real-ffmpeg tests spoil the result on purpose (a too short output, the audio removed, the audio times moved half a second) and check that the original is intact byte for byte.

Detection: frame times of the first 600 packets and, for videos over 20 s, 300 packets from the middle (ffprobe `-read_intervals`, nothing decoded). Variable when at least 3
intervals and 1 % of them are irregular (more than 1.5 ms or 2 % away from the usual one; MKV/WebM round to the millisecond), or when ffprobe's two rates disagree by more than 1 %
and one interval is irregular. Constant tests: exact times, an MP4 clock (1/30000), MKV/WebM milliseconds (33/34 ms, 16/17 ms), an MPEG-TS clock, frames out of order from B-frames.
Not decided by detection alone: fewer than 12 frames to look at, a single freeze in an otherwise constant video.

### F8 with a real clip (2026-10-29)
Clip: "How to Upload an Image to Wikimedia Commons (A Beginner's Guide 2025).webm" (CC0, 1920x1080 VP9/Opus, 119 s). It is **constant** (30 fps, intervals of 33/34 ms, the
rounding of WebM): the engine says "constant ... left as it is" and touches nothing. A second small clip (CC BY-SA, 25 fps) is constant too. To have real content that is
variable, the first 60 s were turned into a variable copy with ffmpeg (frames dropped in three stretches: intervals 33/67/200 ms, average 16.7 fps, nominal 30 fps).
Running it through the real engine showed two things the synthetic clip had hidden, both fixed:
- The audio (60.0 s) outlasts the video (59.5 s), and the time limit at the video's end cut the audio by 0.49 s. The limit is now the longest of the two.
  ffmpeg stretches the video to the audio end for some files and not for others, so the check accepts a result between the video's own length and the longest one.
- The target was 23.976 (nearest to the 16.7 average) although the video says 30 fps: a video whose own rate is 30 or less keeps that rate (nothing is dropped).
Result (MP4 and ProRes MOV): 1800 frames, every interval 33 ms, 30/1 in both rates, audio 60.0 s. Original untouched (same SHA-256). A test with audio longer than the video
(`Audio_that_outlasts_the_video_is_kept_whole`) keeps this from coming back. `A_real_clip_from_the_user_goes_through_the_engine` runs any file given in `VIDEOPREP_REAL_CLIP`.

## 0.3 round 3, F9 updates of videoprep itself (2026-10-29)
What it does: on opening (at most once a day, can be turned off) and with "Buscar actualizaciones ahora" in Ajustes, videoprep reads GitHub's list of releases
(`https://api.github.com/repos/nicolasrabe/videoprep-next/releases`, the repository is one constant in `UpdateEndpoint.cs`). A strictly newer version (semver) shows a banner
"Hay una versión nueva: X.Y.Z - Ver novedades - Actualizar - Ahora no". While the installed version is 0.x, pre-releases count (everything shipped so far is one).
"Actualizar" always needs the click, does nothing while something downloads, and never asks for administrator rights.

Where the address checks are: only `https`, only `github.com`, `api.github.com` and `*.githubusercontent.com`, default port, no user name, every redirect looked at
(5 hops at most, the client never follows one by itself); the download addresses are built from the tag, not copied from the list; size limits are enforced while reading
(list 2 MB, sums 64 KB, zip 250 MB).

How it installs (Windows): download `videoprep-<tag>-win-x64.zip` and `SHA256SUMS.txt` of the same release; the zip's SHA-256 must be listed and equal (missing or different: nothing is
installed, the page is offered); unpack only `videoprep.exe` (flat zip, no `..`, no drive letters, no second copy, size counted while writing); check that it is a Windows x64 program that
says the release's version; the NEW program is started with `--apply-update` (arguments one by one, never a command string); it waits for the old one to close, copies itself to
`videoprep.exe.new`, checks the hash, moves the old one to `videoprep.exe.old`, the new one to `videoprep.exe`, starts it and watches it for 15 s; if it ends with a non-zero code the old one is put back.
Every file step is retried for about 10 s (antivirus and Windows keep files busy); every failure puts things back; the result is saved BEFORE each program opens so that program tells the person once.
The old copy stays as `videoprep.exe.old` to go back (rename it). The old program does not close until the installer has stayed alive 4 s (a blocked or damaged installer must not leave the person with nothing).
macOS and Linux: the button only opens the downloads page.

What the SHA-256 protects and what it does not: it protects against a damaged download and against a zip swapped while the sums file stays the same. It does NOT protect against someone who controls
the repository or the account (they can change both files) or a compromised CI. The real protection would be to sign `videoprep.exe`; that is for later. Until then Windows shows "Windows protegió su PC" on first open.

Checked by running (all on COPIES in a temporary folder, with its own `VIDEOPREP_HOME`; never the owner's exe, settings, queue, scheduled task or clipboard):
- 90+ unit tests with an in-memory GitHub (versions, hosts, redirects, size limits, zip-slip, zip bomb, wrong/missing checksum, swapped zip, not a PE, wrong version, locked files retried and given up, rollback after a crash, command line round trip).
- A folder where Windows itself denies writing (`icacls /deny`): found out before downloading.
- `tests/desktop/update-replace-check.ps1`, real programs on the real desktop (UI Automation), development builds that say 0.2.9 and 0.3.0, a local server on 127.0.0.1:
  `good` (banner by itself, nothing installed before the click, Actualizar, old closes, new opens from the same folder, `.old` is the old file by hash, "se actualizó a la versión 0.3.0" shown once, Ajustes button says "Ya tenés la última versión"),
  `badsum` (explained, old keeps running, no `.old`/`.new`, nothing left in updates), `nostart` (installer cannot start: the old program does NOT close), `crash` (new one exits with code 7 as it opens: old put back and reopened, told "se cerró sola al abrirse"),
  `lockshort` (`videoprep.exe.new` held 4 s: retried, installed), `locklong` (held longer than the wait: nothing changed, old reopened, told it was busy). All six passed.
  The check found two real defects before shipping: the result was written after the 15 s watch (the new program had already started and never told the person), and a new program that cannot start left nobody running.
- Development-only variables (`VIDEOPREP_UPDATE_URL`, only `http://127.0.0.1`; `VIDEOPREP_TEST_CRASH_VERSION`) exist only under `#if DEBUG`; tests (which CI runs in Release) read the compiled dlls and fail if either text is in a Release build.

NOT verified: a real release on GitHub (none has the new files until one is published, so the real download from github.com was never run); the real reaction of Windows Defender / SmartScreen / third-party antivirus to
the replaced exe; Windows 11; macOS (the build exists but nobody ran it); a signed exe. People who have 0.2.0 have no updater: they must download 0.3.0 by hand once (README and release notes say so).
