# videoprep - project context

## The user
- Video editor working on **YouTube-channel videos for a client**. Windows PC, **Adobe Premiere Pro 26.5.2**,
  Descript (transcripts/subtitles), OBS (no longer wanted for clip capture), Google Drive (delivery).
- GitHub user: **nicolasrabe**; repo `github.com/nicolasrabe/videoprep` (renamed from "Video Downloader").
- Location: Argentina. Wants the tool **free for a handful of friends / editors, funded by donations**.
- Uses Firefox logged in to YouTube; yt-dlp needs browser cookies to avoid "confirm you're not a bot".

## Editing workflow (context for features)
1. Receives a script (Word): narration with YouTube links and hand-made M:SS ranges (can be 1-2 s off).
2. Marks each clip F1, F2... by hand, downloads clips (formerly screen-recorded with OBS), leaving headroom
   (needs 4:15-4:38 -> takes 4:00-5:00). Gets narration audio + looped music track; copies a project template folder.
3. Assembles in Premiere: narration, clips, animated frame (solid color background, rectangle shape, lens-flare
   transitions at both ends, drop shadow, progressive zoom-out; template already built), transitions, effects,
   audio fixes, animated text.
4. Renders clip audio -> Descript transcript -> subtitles (only for the un-narrated clips); fixes timing one by one
   and colors per speaker. Renders and uploads to the client via Google Drive.
- **Clip-window rule (user-defined, for future automation):** within one video, ranges with a gap of **<= 5 minutes**
  share one clip, otherwise separate clips. Clip **starts at the minute-zero before the first range** and **ends at the
  next minute-zero after the last range**. If a range starts exactly on a minute, start **30 s earlier**; if it ends
  exactly on a minute, end **30 s later** (clamp to video bounds). No configurable margin. In a future assembly tool:
  **one F per clip occurrence** (not per video).
- Script-import / "Guion" tab idea and assembly-from-narration idea were explored, then the user said to
  **scratch the script-analysis part** ("no entendiste nada, no importa"). Do not push it; keep the rule above in case it returns.
  The "cuadros de lo que viene" (preview frames of the next clip) must stay manual/creative.

## Product decisions (chronological highlights)
- Started as a yt-dlp PowerShell config + `Descargar-F.ps1` flow (H.264 + AAC MP4 for Premiere; VP9 once broke Premiere).
- Then a WinForms GUI; renamed **Video Downloader -> videoprep** (always lowercase). Versions: 0.0.2 (published),
  0.0.3 (rename), **0.1.0** (new tabbed UI, formats, multi-clip, i18n, themes), **0.1.1** (other sites). 0.x on purpose:
  "we are not even close to the final one".
- Approved UI (mockup in chat, now implemented): single-line link box with gray cue text, clears after paste;
  format block; clips (Premiere timecode HH:MM:SS:FF, several per video); file names (original title / custom / series);
  Options window; Send-to-queue (light orange); bottom row = folder icon, Browse, Save-to box, Download (light green),
  Stop (light red). Pasting another link auto-sends the previous draft to the queue.
- Themes: light, dark, beige, gray, slate blue, custom color. Languages: Spanish, English (default by system language).
- Formats: MP4 (H.264+AAC), MOV (H.264+AAC, remux), MOV (ProRes 422 HQ, PCM 24-bit 48 kHz), WebM (VP9+Opus), MKV;
  audio M4A, MP3, WAV, FLAC, Opus. H.264 YouTube streams cap at 1080p; ProRes/WebM/MKV can go to 4K.
  ProRes does not add quality (source is compressed H.264): it is for smooth editing, files are huge. WebM/MKV may not open in Premiere.
- Other sites (0.1.1): anything yt-dlp supports (Twitch, Kick, X/Twitter, Facebook, Instagram, TikTok, Vimeo...);
  preview via yt-dlp metadata; best available then convert; names fall back to the video id; login needed for private content.

## Vision, platforms, distribution (discussed, not built)
- **Superseded (2026-10-25):** the rewrite is C#/.NET + Avalonia, see `docs/PORT_PLAN.md`. The Tauri text below is kept only as history.
- Today: Windows-only PowerShell/WinForms prototype/spec. Recommended future: **Tauri 2 + TypeScript UI** with yt-dlp/FFmpeg as
  managed sidecars (Electron as the safer-but-heavier alternative); GitHub Actions to build Windows/macOS/Linux; separate
  tested "engine" from UI. A **web version** = local helper (runs yt-dlp/FFmpeg) + browser UI (or static UI talking to localhost);
  a hosted download service is NOT recommended (YouTube blocks datacenter IPs, cost, legal exposure).
- Trust: open source, MIT, no telemetry, SHA-256 per release, CI builds, privacy note (cookies read locally only).
- Signing (user said skip for now): Windows OV cert (~150-300 USD/yr) or SignPath Foundation for OSS (check eligibility);
  Azure Artifact Signing for individuals is US/Canada only; macOS needs Apple Developer Program (~99 USD/yr) to notarize.
- Distribution ideas: GitHub Releases + simple site (GitHub Pages), winget/Scoop/Homebrew/Flatpak later. Store apps
  (Microsoft/Mac App Store) probably reject YouTube downloaders - check before investing.
- Donations: GitHub Sponsors supports Argentina (bank account + tax info, no fee for personal sponsorships per GitHub docs);
  also Ko-fi / Cafecito / Liberapay (check each platform's rules). `repo/.github/FUNDING.yml` is a commented template.
- Name checks done by the user: winget has no `videoprep` package; INPI shows no records; `videoprep.io` showed no server
  (availability NOT confirmed - check a registrar). Trademark registration optional.
- Legal: YouTube ToS forbids downloading outside its features; copyright applies. README disclaimer: for content you own,
  have licensed or have permission to use; not affiliated with YouTube/Google/Twitch/Kick/X/Meta/TikTok. Suggest a lawyer
  consult if the project grows or takes real money. Never bundle yt-dlp/FFmpeg in the repo (downloaded at first run).

## GitHub / release state (last known)
- Repo had files inside a `video-downloader/` subfolder by mistake; user was fixing by uploading the contents of the repo zip to the root.
- Release 0.0.2 published (old name). 0.0.3 / 0.1.0 / 0.1.1 publication status unknown - ask the user.
- Release recipe: `repo/docs/RELEASE_CHECKLIST.md` (tag `vX.Y.Z`, title `videoprep X.Y.Z`, pre-release while 0.x, attach zip,
  paste SHA-256 computed on the user's PC with `(Get-FileHash .\videoprep-vX.Y.Z.zip -Algorithm SHA256).Hash | Set-Clipboard`).
