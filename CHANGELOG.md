# Changelog

Versions below 1.0.0 are early releases: things can change between them.

## Sin publicar (0.3.0, Rondas 1, 2 y 3)
- "Cargar archivo..." (also by dragging a file onto the Download or Queue tab) reads links from .txt, .docx, .md, .html
  and .csv. In Word files it finds hyperlinks, HYPERLINK fields and typed links, also in headers, footers, footnotes
  and text boxes. Google Docs and Outlook redirect links are unwrapped. A review window shows every link with the text
  around it, marks repeats, links already queued, channels and non-video links, lets you type a name per link and save
  into a subfolder named after the file. Always whole videos. Files are only read (size limits, zip-bomb guard, no
  external XML entities, nothing fetched). YouTube titles are filled in afterwards, one at a time, with the public
  oEmbed address only (at most 100 per file).
- Queue editing: rename in the row (double-click or F2), right-click menu, "Cambiar N elementos" (format, resolution,
  names: series, add text, find and replace, with a preview), Duplicate, Undo (Ctrl+Z), "Volver a descargar" and
  "Mostrar en carpeta". Rows being downloaded or done are locked; finished rows are not downloaded again by Download.
- Clip times beyond the video length turn red with "El video dura 38:12" (only a warning).
- Expandable queue rows: a click (or the arrow keys) opens the row with thumbnail, title, channel, date, views,
  description, the link (open/copy) and the planned file name. Data is asked for only for the row that is opened, one
  request at a time.

- **Modo mini** (Ronda 2): el botón "Mini" de Descargar achica videoprep a una línea (nombre del archivo, porcentaje
  como barra de fondo, "N/M"), en cualquier lugar de la pantalla, siempre encima si querés (se prende y apaga en vivo; se
  guarda en Ajustes) y **sin quitarle el teclado al programa donde estás escribiendo**. La ventana grande se minimiza (queda un
  solo botón en la barra de tareas) y vuelve al hacer clic en ese botón. Clic derecho: "Cerrar modo mini", "Siempre encima",
  "Cerrar videoprep" (pregunta si hay algo descargando). Nunca muestra enlaces. Probado en el escritorio real de Windows
  (`tests/desktop/mini-focus-check.ps1`).
- **Enlaces copiados** (Ronda 2, apagado por defecto, en Ajustes): agrega a la cola los enlaces de video claros que copiás,
  mientras la opción esté activada y nada más. Ignora lo que copia el propio programa y los repetidos; más de 20 enlaces de
  una vez quedan esperando tu respuesta (la mini avisa). El contenido copiado nunca se guarda ni se anota en el Registro.
- **Perfiles de formato** (Ronda 2): guardá el formato, la resolución y los nombres con un nombre ("Guardar como perfil...").
  Se usan en Descargar, en "Cambiar N elementos", en la ventana de enlaces encontrados y en Scraping (Explorar y
  suscripciones, con una columna "Perfil" en la tabla). Tres perfiles de ejemplo de solo lectura. Se administran en Ajustes
  (renombrar, borrar, exportar e importar `.json`). Antes de cambiar o borrar un perfil que siguen suscripciones, un cartel
  muestra sus nombres (Cancelar / Continuar). Se guardan en `profiles.json` (guardado atómico, con versión).
- **Lista de fuentes** (Ronda 2, apagada por defecto, en Ajustes): al terminar cada tanda escribe `fuentes-AAAAMMDD-HHmm.csv`
  (o `.txt`) en la carpeta de destino con título, canal, enlace, nombre exacto del archivo, ruta, formato, fecha y estado. CSV
  con BOM y con el separador del idioma de Windows; sin cookies ni datos de sesión en los enlaces; los textos que Excel
  tomaría por una fórmula llevan un apóstrofe. También "Exportar lista de fuentes..." en el menú de la cola.

- **Velocidad constante** (Ronda 3, apagada por defecto): una casilla en Descargar (y en Opciones, en los perfiles, en "Cambiar N
  elementos" y en Scraping) que convierte los videos de velocidad variable (Twitch, X, capturas de pantalla) a velocidad
  constante para que Premiere los lea bien. Solo toca un video si **de verdad** es de velocidad variable (mira los tiempos de los
  fotogramas); los de velocidad constante no se recodifican nunca y quedan idénticos. Se hace en el mismo paso que el corte
  exacto de fragmentos, para MP4, MOV (H.264 y ProRes), WebM y MKV; la velocidad nueva es la estándar más cercana (23,976, 24, 25,
  29,97, 30, 50, 59,94 o 60). Antes de reemplazar nada se comprueba la cantidad de fotogramas, la duración (un fotograma de
  margen), que el audio siga en sintonía con el del original y que el resultado sea de verdad constante; si algo falla, el original
  queda intacto y la fila pasa a "Revisar" con el motivo. Un MP4 convertido queda con su nombre limpio (no "Nombre_2").

## 0.2.0 - 2026-10-26
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
  Instagram, X and TikTok profiles cannot be listed today (yt-dlp does not support it); Explore says so and explains
  what to do instead. Twitch, Kick and Vimeo profiles work. Every table's columns can now be widened by dragging the
  header border.
- Phase 5 (second part): Scraping > Subscriptions works. Add a channel or link (or "Subscribe to this..." from Explore)
  with the same filters, format, folder and naming; the first time it either takes only what comes later (default) or
  also downloads what already matches the filters. "Check now" / "Check all", check when videoprep opens and every N
  hours while open. Each subscription has its own history, so nothing is downloaded twice. Gentle with the sites
  (newest 100 per tab, pauses between subscriptions and between downloads, at most 25 new videos per check) and it stops
  at a bot check. New `videoprep --check` runs the check without the window (log in `logs\check-YYYYMMDD.log`, exit
  codes 0 ok / 1 something failed / 2 blocked / 3 busy) and "Set up..." creates a Windows scheduled task for it after
  showing the exact command. The window and the check never run at the same time.
- Fixes from the owner's first test: "Max. count" now means the N newest videos that match (already downloaded ones are
  left out after that), so a subscription no longer downloads two older videos on every check. Videos whose title has
  symbols such as ":" or "?" (saved as "：" / "？") or emoji were downloaded but reported as "skipped" and could not be
  cut into clips; videoprep now gets the exact file name from yt-dlp.

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
