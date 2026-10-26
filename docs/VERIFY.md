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

Still to verify later (needs a later phase): frame-exact clip cut with input-side `-ss` + `-frames:v` (phase 3 integration
test), Chrome/Edge/Firefox cookie reading on Nico's PC (with permission), 429 / "not a bot" messages (only seen when they happen).
