# videoprep - backlog

## 0.1.2 (confirmed by the user, not built yet)
1. **Delete key in the queue:** pressing Supr/Delete removes the selected rows' items (same as "Quitar"; consider no confirmation; keep selection sane afterwards). Hook `KeyDown` on `$grid`.
2. **Subscriptions + "scraping mode"** (new tab, e.g. "Canales"):
   - Subscribe to channels (YouTube first, other sites later); on app start (and optionally on a timer while open, or via a Windows scheduled task running the script headless) fetch new videos and download them automatically.
   - Pick playlists or whole channels (also shorts/streams tabs) and choose what to download with filters: **publication date range**, title include/exclude, min/max duration, skip live/members, max count, newest/oldest first, format profile and destination folder per subscription.
   - Suggested design: two modes - (a) *list first*: fetch flat metadata (title, date, duration, views, thumbnail) into a table with checkboxes -> "send to queue"; (b) *subscribe*: saved subscription JSON in `%APPDATA%\videoprep\subscriptions.json` with a per-subscription yt-dlp archive file so nothing is downloaded twice.
   - yt-dlp building blocks to verify against the current docs: `--flat-playlist`, `--dateafter/--datebefore`, `--match-filters`, `--playlist-items`, `--max-downloads`, `--download-archive`, `--break-on-existing`, `--sleep-interval/--sleep-requests`. Be gentle with rate limits; use the cookie chain; warn about account risk when many requests are made while logged in.
   - Respect the legal notice (own/licensed/permitted content).
3. **Preview robustness for other sites:** show a clear state when the yt-dlp metadata run fails or times out (today it can stay on "Cargando..."), write the yt-dlp error to the Log tab, retry once without cookies, keep the queue grid title in sync. (User saw a Twitch clip with no thumbnail/title once; it worked later - root cause unknown.)
4. Fixes from the user's real testing of 0.1.0/0.1.1 (ask for the Log tab output).

## Planned / discussed later
- Parallel downloads; playlist expansion with checkboxes (merges with the subscription list view).
- Linux and macOS (and a web version via local helper). Desktop rewrite with Tauri 2 + TypeScript; engine tested separately; CI builds for 3 OSes.
- Signed releases (see PROJECT.md), winget/Scoop packaging, project website, donation links in README and Settings.
- Premiere/Descript workflow ideas (only if the user raises them again): clip-window rule from PROJECT.md; Premiere UXP plugin to place clips (ExtendScript support was announced to end Sept 2026; UXP API coverage in 26.5.2 unverified); subtitles QC rules; per-speaker caption tracks.
- Option for ProRes profile (Proxy/LT/422/HQ); clip quality presets; per-link output folder.

## Scratched by the user (do not pursue unless asked)
- "Guion" tab: import the narration script (.docx), auto-number F per clip, group into windows, download. The user said the analysis missed the point.
- OBS screen-recording automation. "Cuadros de lo que viene" automation.

## Known risks / open questions
- Untested on Windows by the assistant (see ARCHITECTURE.md). Hash of release zips changes with each build.
- Docx scripts exported from Google Docs may lack comments.
- Premiere may not open WebM/MKV. Frame-accurate clips depend on constant frame rate sources.
- Platform terms (YouTube, Instagram, etc.) and rate limits; yt-dlp breaks when sites change -> "Actualizar herramientas" button / weekly check option.
