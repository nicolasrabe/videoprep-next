# Changelog

Versions below 1.0.0 are early releases: things can change between them.

## 0.2.0 (in progress)
- Rewrite as a C#/.NET + Avalonia app that builds to a single `videoprep.exe` (no PowerShell needed).
- Phase 1: project skeleton, automatic build and tests on Windows, macOS and Linux (GitHub Actions), test window showing the version.
- Phase 2: the rules of 0.1.1 moved to tested C# code: timecode and frame math (drop-frame included), clip planning,
  link recognition, file names, series numbering, yt-dlp/ffmpeg commands, ffprobe reading, settings, Spanish/English texts.
- Fixes in this logic: an 11-character word is no longer taken for a YouTube ID unless it is alone on its line; safer
  file names (reserved Windows names, trailing dots); audio preview no longer asks for H.264 video; variable frame rate
  is detected; clips seek faster on long videos.
- Phase 3: the engine. It downloads each video once, cuts every clip from it, checks each result with ffprobe before
  moving it to its final name, and only then deletes the downloaded full video (never one that was already in the folder).
  Stop also closes any program yt-dlp started. Tools are downloaded to `%LOCALAPPDATA%\videoprep\tools`. The queue is
  saved to `queue.json`. Unexpected errors are written to a log with all details.
- New developer command `--demo` to watch the engine work from the terminal (docs/DEMO.md).
- Phase 4: the screens. Download, Queue, Settings and Log tabs with everything from 0.1.1, plus a preview of the new
  Scraping tab (sample data, it starts working in the next phase). The window can be resized (minimum 900x640).
  Six themes (also for check boxes and option buttons), Spanish and English that switch at once, drag and drop of links
  and .txt files, Delete key in the queue, "Copy diagnostics" in the Log tab, and a friendly message if something
  unexpected happens. The preview always ends in a clear state (title, "no H.264", or why it failed) instead of
  staying on "Loading". The queue is kept when videoprep closes. Settings from 0.1.1 are read as they are.
- Phase 5 (first part): Scraping > Explore works. Paste a channel, playlist or other-site link, pick the tabs
  (Videos, Shorts, Live, Playlists), press Find videos and the list fills in as it arrives. Filters (dates, length, words
  in the title, count, order, skip live / members-only / already downloaded) re-filter the saved list without asking the
  site again. Dates from the quick list are approximate (~); when a date or length filter needs it, exact data is
  fetched for at most 100 videos. Checked videos go to the queue with their titles and the chosen format. A bot check
  stops the search with a clear message. Searches use no cookies unless one browser or a cookie file is chosen in Settings.
  Subscriptions come in the second part.

## 0.1.1
- Links from other sites: Twitch (clips and videos), Kick, X/Twitter, Facebook, Instagram, TikTok, Vimeo and any other site supported by yt-dlp.
- Preview for those sites (title, author, length, thumbnail and quality) read through yt-dlp.
- Other sites are downloaded as best available and converted to the chosen format when needed.
- File names fall back to the video ID when a site gives no title.
- Hints for sites that need a login; the legal notice now covers the other platforms.
- Safer link recognition: only real YouTube links are treated as YouTube (Facebook video numbers are no longer mistaken for YouTube IDs).

## 0.1.0
- New Download tab: single-line link box (clears after pasting), format block, clips, file names, Options window and Send to queue.
- Several clips per video (frame-accurate Premiere timecode); the video is downloaded once and cut as many times as needed.
- New formats: MP4, MOV (H.264 or ProRes 422 HQ), WebM, MKV; audio-only as M4A, MP3, WAV, FLAC or Opus.
- Pasting a new link sends the previous one to the queue automatically.
- Queue tab: edit, remove, reorder, retry failed, load a .txt file.
- Full Spanish and English interface; language selector in Settings.
- Themes: light, dark, beige, gray, slate blue and a custom color.
- The Options tab became a window; Download, Stop, Open folder and Browse were rearranged and colored.
- The script is now saved as UTF-8 with BOM so accents display correctly.

## 0.0.3
- The project is now called **videoprep** (it was "Video Downloader"): new file names (`videoprep.bat`, `videoprep.ps1`), window title and documentation.
- Settings and the "already downloaded" history from older versions are copied over automatically.
- No other changes.

## 0.0.2 (released as "Video Downloader")
- New tabbed window: Download, Queue, Options, Settings, Log.
- Queue tab: per-link clips with Premiere timecode `HH:MM:SS:FF` (frame-accurate, last-N-frames with a minus sign, drop-frame with semicolons).
- Instant link recognition on paste (full, `youtu.be`, Shorts, bare ID) and a preview panel.
- Audio-only modes, resolution cap, H.264 fallback conversion, subtitles, thumbnails, speed limit, skip already-downloaded videos.
- Retry failed, reorder and remove links, drag and drop.
- Log tools: Copy log, Save log, Test setup.
- First-run tool download (yt-dlp, FFmpeg, Deno).

## 0.0.1
- First prototype: one window to paste links, choose a name style and a folder.
