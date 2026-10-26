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

Seen only on the CI (Ubuntu's own ffmpeg, not the yt-dlp FFmpeg build): `ffprobe -count_frames` on a WebM clip printed
"Error parsing Opus packet header" and sometimes no count. The tests now count video packets without decoding. Not seen
with the Windows build videoprep downloads; to look at again if a user reports broken Opus audio in WebM clips.

Still to verify later: Chrome/Edge/Firefox cookie reading on Nico's PC (with permission), 429 / "not a bot" messages (only seen when they happen).
