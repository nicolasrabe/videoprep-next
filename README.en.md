# videoprep

**[Español](README.md)**

Free tool for video editors: paste a link (YouTube and any site yt-dlp supports), pick format, clips and names, and
download files ready to edit in Premiere Pro, DaVinci Resolve or whatever you use.

Current version: **0.3.0** (test release, Windows). It is a single `videoprep.exe`; nothing to install.

## What it does
- **Download**: MP4 (H.264 + AAC), MOV (H.264 or ProRes 422 HQ), WebM, MKV, or audio only (M4A, MP3, WAV, FLAC, Opus),
  with a resolution cap. Preview with title, length and available quality.
- **Clips** with `HH:MM:SS:FF` timecode (several per video). The end frame is included; every clip is checked with
  ffprobe before it is saved. `-00:00:00:15` = the last 15 frames.
- **Names**: original title, custom title or series (name + number, continuing after what is already in the folder).
- **Queue**: several links at once, edit, move, remove (Delete key), retry the failed ones. Kept when you close.
  You can **rename** a row (double-click or F2), **change several items at once** (format, resolution, names), duplicate,
  **undo** (Ctrl+Z), download again, and open any row to see thumbnail, title, channel, date and description.
- **Load a file with links** (`.txt`, `.docx`, `.md`, `.html`, `.csv`, or drag it in): videoprep finds the links, shows them for
  review (repeats, already queued, not videos) and adds them to the queue. The file is only read.
- **Format profiles**: save format, resolution and names under a name and use them in Download, the queue and Scraping.
- **Constant frame rate** (checkbox, off by default): videos with a variable frame rate (Twitch, X, screen recordings) cause trouble in
  Premiere. This option only touches the ones that **really** are variable and makes them constant, in the same step as the cut; before
  replacing anything it checks frames, length and audio. If something does not add up, the original stays untouched.
- **Mini mode**: one line with the progress that does not take the keyboard from the program you are typing in.
- **Copied links** (off by default): adds the video links you copy to the queue, only while the option is on.
- **List of sources** (off by default): when a batch ends it saves a `.csv` or `.txt` with title, channel, link and file name.
- **Updates**: tells you when a new version exists and installs it when you press **Update** (see below).
- **Scraping → Explore**: paste a channel or playlist, see all its videos in a table, filter by date, length, words in the
  title and count, and send the ones you check to the queue.
- **Scraping → Subscriptions**: videoprep checks the channels you choose and downloads only what is new, never twice.
  It can check when it opens, every N hours, or with videoprep closed (Windows scheduled task).
- Spanish and English, six color themes, every table column can be widened.

## Download and open
1. From [Releases](https://github.com/nicolasrabe/videoprep-next/releases) download `videoprep-v0.3.0-win-x64.zip` and
   unzip it into a folder of yours (for example `Documents\videoprep`).
2. Open `videoprep.exe`. The program is not signed, so Windows may say "Windows protected your PC": click
   **More info**, then **Run anyway**.
3. The first time you press **Download**, videoprep fetches the tools it needs (yt-dlp, FFmpeg and Deno, about 200 MB,
   once) into `%LOCALAPPDATA%\videoprep\tools`. In **Settings** you can update yt-dlp at any time (**Update tools**) or turn on
   "Check for yt-dlp updates automatically (once a week)".

> **If you already have 0.2.0**: that version cannot update itself. Download 0.3.0 by hand once (step 1) and replace the old
> `videoprep.exe` with the new one. From 0.3.0 on, videoprep tells you when a new version exists.

To check the zip was not modified, compare its SHA-256 with the one in `SHA256SUMS.txt` on the same page:
in PowerShell, `Get-FileHash videoprep-v0.3.0-win-x64.zip`.

## Updates
- When it opens (at most once a day) videoprep reads GitHub's public list of versions; in **Settings** you can turn that off and press
  **Check for updates now**. Nothing of yours is sent (only the request to GitHub, which carries videoprep's version number).
- If a newer version exists a banner appears at the top: **See what's new** or **Update**. Nothing is installed until you press
  **Update**, and it does nothing while a download is running.
- **Update** downloads the zip and `SHA256SUMS.txt` of that version, checks the SHA-256 and, if it matches, replaces the program and opens
  it again (your queue and settings are kept). If the checksum is missing or does not match, nothing is installed and it offers to open the page.
- The previous version stays next to it as `videoprep.exe.old`. **To go back**: close videoprep, delete `videoprep.exe` and rename
  `videoprep.exe.old` to `videoprep.exe`. If the new version closes by itself as it opens, videoprep goes back to the old one on its own.
- If the folder of `videoprep.exe` does not allow writing (for example `Program Files`), it says so before downloading anything and does not
  ask for administrator rights: move videoprep to a folder of your own (like Documents) or download the new version by hand.
- What the SHA-256 protects: a damaged download or a swapped zip. It does **not** protect if someone controlled the GitHub repository
  (they could change the zip and the sum together); full protection would be signing the program, which is for later.
  That is why Windows still shows "Windows protected your PC" the first time.
- On Mac and Linux the button only opens the downloads page.

**Windows only for now:** CI and releases cover Windows only (what is left for Mac/Linux is in `docs/PORTING_LATER.md`).

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
- `%LOCALAPPDATA%\videoprep`: tools, logs and the updates being installed.

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
