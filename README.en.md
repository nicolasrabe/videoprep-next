# videoprep

**[Español](README.md)**

Free tool for video editors: paste a link (YouTube and any site yt-dlp supports), pick format, clips and names, and
download files ready to edit in Premiere Pro, DaVinci Resolve or whatever you use.

Current version: **0.2.0** (test release, Windows). It is a single `videoprep.exe`; nothing to install.

## What it does
- **Download**: MP4 (H.264 + AAC), MOV (H.264 or ProRes 422 HQ), WebM, MKV, or audio only (M4A, MP3, WAV, FLAC, Opus),
  with a resolution cap. Preview with title, length and available quality.
- **Clips** with `HH:MM:SS:FF` timecode (several per video). The end frame is included; every clip is checked with
  ffprobe before it is saved. `-00:00:00:15` = the last 15 frames.
- **Names**: original title, custom title or series (name + number, continuing after what is already in the folder).
- **Queue**: several links at once, edit, move, remove (Delete key), retry the failed ones. Kept when you close.
- **Scraping → Explore**: paste a channel or playlist, see all its videos in a table, filter by date, length, words in the
  title and count, and send the ones you check to the queue.
- **Scraping → Subscriptions**: videoprep checks the channels you choose and downloads only what is new, never twice.
  It can check when it opens, every N hours, or with videoprep closed (Windows scheduled task).
- Spanish and English, six color themes, every table column can be widened.

## Download and open
1. From [Releases](https://github.com/nicolasrabe/videoprep-next/releases) download `videoprep-v0.2.0-win-x64.zip` and
   unzip it into a folder of yours (for example `Documents\videoprep`).
2. Open `videoprep.exe`. The program is not signed, so Windows may say "Windows protected your PC": click
   **More info**, then **Run anyway**.
3. The first time you press **Download**, videoprep fetches the tools it needs (yt-dlp, FFmpeg and Deno, about 200 MB,
   once) into `%LOCALAPPDATA%\videoprep\tools`. In **Settings** you can update yt-dlp at any time (**Update tools**) or turn on
   "Check for yt-dlp updates automatically (once a week)".

To check the zip was not modified, compare its SHA-256 with the one in `SHA256SUMS.txt` on the same page:
in PowerShell, `Get-FileHash videoprep-v0.2.0-win-x64.zip`.

## Sites
YouTube (videos, shorts, live, playlists), Twitch, Kick, Vimeo, X, Instagram, Facebook, TikTok and
[any site yt-dlp supports](https://github.com/yt-dlp/yt-dlp/blob/master/supportedsites.md).
**Explore** and **Subscriptions** work with YouTube channels and playlists and with Twitch, Kick and Vimeo profiles.
Instagram, X and TikTok profiles cannot be listed today (yt-dlp does not support it); their single posts download fine.

Some videos need a login. In **Settings** you choose which browser to read cookies from (yt-dlp reads them on your PC;
they are not sent anywhere) or a `cookies.txt` file. For public channels prefer no cookies: many requests while logged
in can put your account at risk.

## Automatic check without the window
`videoprep.exe --check` checks the enabled subscriptions and exits. The scheduled task created by **Set up...** uses it
(you see the exact command before it is created; **Remove task** deletes it). It writes
`%LOCALAPPDATA%\videoprep\logs\check-YYYYMMDD.log` and exits with 0 (ok), 1 (something failed), 2 (the site asked for
verification and the check stopped) or 3 (another download or check was running).

## Where things are kept
- `%APPDATA%\videoprep`: settings, queue, subscriptions and the download history.
- `%LOCALAPPDATA%\videoprep`: tools and logs.

To uninstall: delete the `videoprep.exe` folder, those two folders and, if you created it, the scheduled task
(**Remove task**).

## If something fails
Go to the **Log** tab, press **Copy diagnostics** and paste it in an
[issue](https://github.com/nicolasrabe/videoprep-next/issues). The diagnostics never include cookies.

## For developers
Needs the .NET 10 SDK. `dotnet build videoprep.slnx`, `dotnet test videoprep.slnx`, and for the exe:
`dotnet publish src/Videoprep.App -c Release -r win-x64 -o dist/win-x64`. The version lives in `Directory.Build.props`.
The previous version (0.1.1, PowerShell) is in `legacy-ps/`.

## Legal notice
Use it only with content you own, have licensed or have permission to use. videoprep is not affiliated with YouTube,
Google, Twitch, Kick, X, Meta or TikTok. No telemetry: login cookies are read locally by yt-dlp on your PC.

MIT license.
