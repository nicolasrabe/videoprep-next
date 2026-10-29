# videoprep 0.3.0 - specification

Target version 0.3.0 (bump at the end). Windows-only, as in 0.2.0. UI mockups (open them in a browser, they are interactive)
are in `docs/mockups-v0.3/`: `mockup-mini-e-importar.html`, `mockup-editar-cola.html`, `mockup-cola-expandible.html`.
**Where text and mockup disagree, this file wins.** All UI text in Spanish (no anglicisms: "enlace", "cola", "fotogramas") and
English, in the existing i18n files, themes (6) and languages (es/en) respected by every new screen.
Do not break anything from 0.2.0. Every new setting gets a safe default and loads from older `settings.json` files.

Work in three rounds with a stop after each (see the end). Feature IDs: F1..F11.

## F1. Load a file with links ("Cargar archivo...")
- The Queue button "Cargar .txt..." becomes "Cargar archivo..."; the same flow works by dragging a file onto the Download or Queue tab.
- Supported: `.txt`, `.docx`, `.md`, `.html`/`.htm`, `.csv`. For `.doc` and `.pdf` show a friendly message ("Por ahora no se leen estos archivos; guardalo como .docx o .txt").
- Extraction:
  - Plain text: bare URLs (`https://...`, `www....`, `youtu.be/...`) and the sites already recognized by the link parser.
  - `.docx` (a zip; parse `word/document.xml`, `word/_rels/document.xml.rels`, headers, footers, footnotes): hyperlinks (`w:hyperlink` with `r:id` resolved
    through relationships), `HYPERLINK` field codes (`w:instrText`), and URLs typed as text. Keep the visible text of the hyperlink as "text where it was".
  - `.md`: `[text](url)` and bare URLs. `.html`: `href` values with their anchor text. `.csv`: any cell containing a URL.
- Safety: files are read-only and only parsed, never opened by other programs. Limit input size (default 20 MB) and uncompressed docx size
  (zip-bomb guard); XML parsing with DTD/external entities disabled. No network access during parsing.
- Normalize and de-duplicate: `youtu.be/ID` = `watch?v=ID` = `shorts/ID`; ignore tracking parameters (`si`, `utm_*`, `feature`) when comparing but keep
  what the downloader needs. Mark duplicates inside the file and links already in the queue.
- **Review window "Enlaces encontrados en <archivo>"** (see mockup): columns checkbox, Enlace, Nombre (editable; default "título original"),
  Texto donde estaba, Cómo estaba (Texto / Hipervínculo), Estado (YouTube, Twitch, X, ... / Repetido / Ya en la cola / No compatible).
  Non-video or unsupported links start unchecked and are explained. Buttons Todo/Nada/Invertir, a "Formato" selector (default: the current Download tab
  selection or a saved profile, see F6), a checkbox "Guardar en una subcarpeta con el nombre del archivo" (off by default; sanitized folder name), Cancelar, "Agregar N a la cola".
- **Always whole videos.** Never try to infer clips or timecodes from the document. Clips remain a manual choice via Editar.
- If a Name was typed, the item uses NameMode custom with that name (existing rules for duplicates/safe names).

## F2. Edit items already in the queue
- Rename in the row: double-click the name or F2; Enter confirms, Esc cancels. It sets the item's custom name; for items with several clips the files are "Nombre - 1", "Nombre - 2".
- Right-click menu (see mockup): Renombrar (F2), Cambiar formato o nombres de los seleccionados..., Editar en la pestaña Descargar (existing behavior, one item), Duplicar,
  Subir/Bajar, Volver a descargar (only "Listo" rows), Mostrar en carpeta (only "Listo" rows; open Explorer with the file selected), Quitar (Supr).
- Rows "Descargando" or "Listo" are locked for editing (lock icon) but can still be opened (F3) and removed/re-queued.
- "Cambiar N elementos" dialog: Formato, Resolución máx. and Nombres, each with "(sin cambios)". Naming options: Serie (name + number, continuing existing numbering as today),
  agregar texto al principio o al final, buscar y reemplazar. Always show a live preview of the resulting names before applying. Also lets you apply a saved profile (F6).
- Undo: Ctrl+Z / "Deshacer" in the Queue reverts the last queue edit (one level is enough; more is welcome).
- Fragment warning: when a clip end/start is beyond the known video duration, color the field red and explain "El video dura 38:12" (only when the duration is known; never block the user).

## F3. Expandable queue rows
- Clicking a row (or moving with the arrow keys) expands that same row in place to show: large thumbnail with duration, title, channel, date, views, description (scrollable, capped around 4000 chars), the link
  (click opens it in the default browser; only http/https allowed), buttons "Abrir en el navegador" and "Copiar enlace", and "Se descargará: ..." with the planned format and final file name.
- Only one row open at a time: opening another closes the previous one; clicking the open row closes it. With several rows selected, nothing expands.
- Data is fetched lazily when a row is opened (never prefetched for the whole queue): reuse the preview cache; the description needs a yt-dlp metadata call (cookie chain as today, one retry without cookies).
  States: cargando (skeleton), listo, no se pudo leer (explain why, button Reintentar, details in the Log). Cache for the session. Respect rate limits (one request at a time).
- Implementation hint: Avalonia DataGrid `RowDetailsTemplate` with `RowDetailsVisibilityMode=VisibleWhenSelected` fits; any equivalent that keeps keyboard and screen-reader access is fine.

## F4. Mini mode ("variante B", one line)
- Button "Mini" in the bottom row of the Download tab (next to Descargar/Detener) toggles it; the big window hides while mini is active and returns from the "⤢" button or by closing the mini window.
- Mini window: fixed small size (about 300x28), no taskbar clutter beyond one icon, draggable anywhere, remembers position and pin state. Shows the current file name (ellipsis) and the percentage;
  the progress is drawn as a filled background behind the text. Shows "N/M" when there are several videos.
- Buttons inside it: pin "📌" (siempre encima / not always on top, toggles live) and "⤢" (volver a la ventana grande).
- States: downloading (name + %), idle ("Todo listo · N de N terminados"), problems (red text "Terminado: X bien, Y con error"), setup/update running. Inherits theme and language.
- Always-on-top must not steal keyboard focus (do not activate on show). Setting in Ajustes: "La ventana mini queda siempre encima" (default on). Never show links or cookies in it.
- Platform-specific window flags go behind the existing `Platform` abstraction so the Core and tests stay OS-independent.

## F5. Clipboard watcher (Ajustes)
- Setting (default OFF): "Reconocer y agregar a la cola automáticamente los enlaces que copio". When ON and the app is running (also minimized or in mini mode), copied text containing supported video links
  is added to the queue as default items (whole video, current default format). Ignore duplicates and links the app itself copied; debounce; text only. Show a brief message in the status line / mini window
  ("Se agregó 1 enlace a la cola"). Do not log clipboard content. Short privacy note next to the setting ("videoprep lee el portapapeles solo mientras esta opción esté activada").

## F6. Custom format profiles
- Save the current selection as a named profile (kind, format, max resolution, "velocidad constante" flag (F8), naming mode and series prefix). Built-in examples (read-only): "Premiere 1080p" (MP4), "ProRes 4K", "Solo audio (M4A)".
- Profile picker in the Download tab; usable from the batch-edit dialog (F2), the import window (F1) and in Scraping subscriptions. Manage (rename, delete, export/import as `.json`) in Ajustes.
  Stored in `profiles.json` (atomic write, version field). A deleted profile used by a subscription falls back to its stored values.

## F7. Sources list (optional)
- Setting (default OFF): "Guardar una lista de fuentes al terminar cada tanda", format CSV (UTF-8 with BOM, opens in Excel) or TXT. Written into the destination folder (or the F1 subfolder) as `fuentes-AAAAMMDD-HHmm.csv`.
- Columns: Título, Canal, Enlace, Nombre del archivo guardado (the exact final file name), Ruta, Formato, Fecha de descarga, Estado. One row per output file (a clip counts as its own row).
- Also a manual "Exportar lista de fuentes..." in the Queue menu for finished rows. Escape CSV correctly; never include cookies or tokenized URLs (strip query parameters that look like tokens).

## F8. "Normalizar a velocidad constante" (variable frame rate fix)
Premiere handles variable-frame-rate (VFR) sources badly (Twitch clips, X, screen recordings). Option in Opciones and in profiles, default OFF.
- Detect VFR first (ffprobe: `avg_frame_rate` vs `r_frame_rate`, plus a bounded sample of packet timestamps). **Only touch files that are really VFR**; constant-rate files are left alone (no needless re-encode).
- Fix with ffmpeg `-fps_mode cfr -r <target>` (do NOT use the deprecated `-vsync`). Target rate = detected average snapped to a standard rate (23.976, 24, 25, 29.97, 30, 50, 59.94, 60), NTSC rates as exact fractions (`30000/1001`). This forces a video re-encode (no stream copy), so the log must say so, and it uses the existing CRF setting.
- Combine with clip cutting in the SAME ffmpeg pass; compute frame math against the target rate and document in the log what was done ("variable → 30 fps constante").
- Known risks to test and report: audio/video drift, duplicated or dropped frames (visible stutter), duration changing by more than one frame, MP4 timescale for NTSC rates, ProRes/WebM compatibility, extra generation of compression, slower processing. If any check fails, keep the original file and mark the row "Revisar".
- A quick check in a sandbox with ffmpeg 6.1 showed: `-fps_mode cfr -r 30` gives `r_frame_rate = avg_frame_rate = 30/1` and the right frame count, a cut in the same pass gave exactly 90 frames for 3 s, `30000/1001` and ProRes work, and `-vsync` prints a deprecation warning. That test file was synthetic and NOT a faithful real VFR clip, so the real validation is the integration test below.
- Integration tests (opt-in `Integration`): generate a true VFR clip with matching audio and video duration; after normalization assert constant rate, frame count = duration x fps within +-1, audio and video durations within one frame, exact frame count for a cut, success for MP4, MOV (H.264) and ProRes, and untouched output for an already constant-rate input.

## F9. Update notice and one-click update
- Check GitHub Releases of the repository that publishes the releases (constant in one place) at startup and then at most once per day, if "Buscar actualizaciones de videoprep automáticamente" is ON (default ON). Only an anonymous GET of the public API; no other data is sent.
- **Important:** all current releases are marked pre-release, so the check must include pre-releases (setting "Incluir versiones de prueba", default ON while the major version is 0). Compare versions as semantic versions.
- Banner at the top of the window: "Hay una versión nueva: X.Y.Z · Ver novedades · Actualizar". "Ver novedades" opens the release page in the browser.
- "Actualizar" (always needs the user's click): download the Windows zip asset into `%LOCALAPPDATA%\videoprep\updates`, verify its SHA-256 against a checksum published with the release (extend `release.yml` to attach `SHA256SUMS.txt` and mention it in the notes; refuse to install if it does not match), extract, then replace the running exe safely:
  the new exe is started with an `--apply-update` argument, waits for the old process to exit, swaps files (keeping `videoprep.exe.old` for rollback), and restarts. If anything fails, show a clear message and keep the current version.
- If the exe sits in a folder that needs admin rights, explain it and offer to open the release page instead. A separate "Buscar actualizaciones ahora" button in Ajustes. Unit-test version comparison, checksum verification and the swap logic with fake files.

## F10. Small additions (in the rounds below)
Mostrar en carpeta (F2), fragment-out-of-range warning (F2), subfolder per imported file (F1). Nothing else.

## F11. Out of scope for 0.3.0 (do not build)
Automatic clips or timecodes from documents; naming templates; parallel downloads; dragging files out of the queue into Premiere; macOS build; exe signing; toast notifications.
(Parallel yt-dlp processes are technically possible but add rate-limit and file-conflict risk; deliberately postponed.)

## Rounds
**Ronda 1 (queue):** F1, F2, F3. **Ronda 2:** F4, F5, F6, F7. **Ronda 3:** F8, F9, then release 0.3.0 (README es/en, CHANGELOG, version, release notes, checklist, SHA256SUMS).
Stop after each round: build, test, screenshots of the new screens in all themes and both languages, an honest list of what could not be verified, then wait for approval.

## Acceptance (summary)
Every feature has unit tests, headless UI tests where it has a screen, es/en strings for everything, and passes CI on Windows, macOS and Linux (platform-specific parts behind `Platform`). Loading a real `.docx` with typed links and hidden hyperlinks
extracts both. Expanding rows never fires more than one metadata request at a time. The mini window never steals focus. No setting that reads the clipboard or the network is ON by default except the update check.
