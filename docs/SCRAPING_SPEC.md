# Scraping tab - specification (0.2.0; was backlog 0.1.2 #2)

Tab name in the UI: **Scraping** (same word in es/en). Mockup: `docs/mockup-scraping.html` (approved by the owner "for now";
two sub-views: **Explorar** and **Suscripciones**). The window must be resizable so the tables breathe.
Legal notice applies (own/licensed/permitted content) and is shown once on first use of the tab.

## 1. Explorar ("list first")
Flow: paste a channel / playlist / profile URL -> **Buscar videos** -> table with checkboxes -> **Enviar a la cola** or
**Suscribirme a esto...** (creates a subscription pre-filled with the same filters).

Inputs
- Source URL. YouTube channel (`@handle`, `/channel/ID`, `/c/`, `/user/`), playlist (`list=`), and other sites that yt-dlp
  can list (Twitch channel clips/videos, Vimeo, etc.; best effort, show a clear error otherwise).
- Tabs to include (YouTube channels): Videos (`/videos`), Shorts (`/shorts`), Directos (`/streams`), Listas (`/playlists`;
  selecting a playlist row expands into its videos). Only the checked tabs are listed.
- Filters (all optional): published from/to (date pickers, `YYYY-MM-DD`), duration min/max (`M:SS`/`H:MM:SS`), title
  includes (comma separated, ANY match), title excludes (comma separated, ANY match, case-insensitive, accent-insensitive),
  max count, order (newest first / oldest first), skip live, skip members-only, skip already downloaded (archive).
- Format profile: reuse the Descargar format/resolution selectors (stored per subscription when subscribing).

Table columns: checkbox, Titulo, Fecha, Duracion, Vistas, Estado (Nuevo / Ya descargado / En cola / Directo / Miembros).
Under it: Todo / Nada / Invertir, "N de M seleccionados", format selector, **Enviar a la cola**, **Suscribirme a esto...**.
Optional thumbnail column or hover preview (nice to have, last).

Listing mechanics (VERIFIED 2026-10-26 with yt-dlp 2026.10.21: see `docs/VERIFY.md`)
- Fast listing: `yt-dlp --flat-playlist -J --no-warnings <tab url>` (or `--print` with a template) gives id, title, duration,
  view count, url quickly without hitting each video. In flat mode the upload date is often missing.
- Dates: try `--extractor-args "youtubetab:approximate_date"` for approximate date in flat mode (VERIFIED: exists, fills `timestamp`; see docs/VERIFY.md; it exists in the
  installed version). If a date filter is active and dates are missing, fetch exact metadata only for the entries that pass
  all the other filters, with `--skip-download --print ...` in small batches and a cap (default 100), showing progress.
  Show approximate dates with a "~" and confirm the exact date at download time with `--dateafter`/`--datebefore` as a safety net.
- Server-side options available: `--playlist-end N`, `--lazy-playlist`, `--dateafter/--datebefore YYYYMMDD`,
  `--match-filters` (e.g. `!is_live`, duration/availability expressions; VERIFIED: missing fields pass `!`/`!=` tests), `--break-match-filters`,
  `--max-downloads`. Prefer client-side filtering of the flat list (flexible, instant re-filter without new requests); use the
  yt-dlp options as a second guard when actually downloading.
- Cancel button for long listings; results stream into the table; cache the list per URL for the session ("Aplicar filtros" re-filters locally).

## 2. Suscripciones ("subscribe and forget")
Data: `%APPDATA%\videoprep\subscriptions.json` (macOS: `~/Library/Application Support/videoprep/`).
```json
{ "version": 1, "settings": { "checkOnStart": true, "everyHours": 0, "scheduledTask": false,
                              "maxNewPerCheck": 25, "pauseBetweenSubsSec": [5, 15] },
  "subscriptions": [ {
    "id": "sub_a1b2c3", "name": "@canal-de-ejemplo", "url": "https://www.youtube.com/@canal-de-ejemplo",
    "enabled": true, "tabs": ["videos", "streams"],
    "filters": { "from": "2026-09-01", "to": null, "minSec": 120, "maxSec": 2700, "include": ["entrevista"],
                 "exclude": ["short"], "maxCount": 50, "order": "newest", "skipLive": true, "skipMembers": true },
    "profile": { "kind": "video", "fmt": "mp4", "cap": 1080, "naming": "orig", "prefix": "" },
    "folder": "D:\\Videos\\canal-de-ejemplo", "subfolderPerSub": false,
    "firstRun": "markExistingAsSeen",     // or "backfillWithFilters"
    "lastCheck": "2026-10-25T09:14:00-03:00", "lastNew": 2, "lastError": null } ] }
```
Per-subscription archive: `%APPDATA%\videoprep\archives\<id>.txt` in yt-dlp archive format (`youtube <videoId>` per line,
VERIFIED: `youtube <id>`) and passed with `--download-archive`. Nothing is ever downloaded twice per subscription.

First run choice when subscribing (radio in the dialog, default **only new from now**): (a) mark everything currently listed
as seen (write the IDs to the archive without downloading), or (b) backfill what passes the filters (capped by maxCount).

Check procedure (`CheckRunner`, used by the UI button, on-start, timer and `videoprep --check`)
1. For each enabled subscription (sequential): list newest entries with the flat listing, stop early once a page is fully
   already-archived (`--break-on-existing` semantics or own logic), apply the filters, drop archived IDs.
2. Enqueue the new items as normal jobs (same engine: formats, clips-less, naming, folder) with the subscription profile.
   Respect `maxNewPerCheck` (rest wait for the next check; tell the user in the log).
3. Update `lastCheck`, `lastNew`, `lastError`; append IDs to the archive only after a successful download.
4. Rate limiting: pause between subscriptions (random 5-15 s), `--sleep-requests 1-2`, `--sleep-interval 3 --max-sleep-interval 8`
   between downloads, back-off (stop the run) on HTTP 429 or "Sign in to confirm you are not a bot" and report it. Never loop.
5. Summary: a Log section and a toast/notification in the UI ("2 videos nuevos de @canal-de-ejemplo"); headless run writes
   `%APPDATA%\videoprep\logs\check-YYYYMMDD.log` and exits 0 (ok), 1 (some failed), 2 (blocked/rate-limited),
   3 (skipped: another check or download run holds the lock; decided in Phase 0).

Triggers
- **Revisar ahora** button (one or all). **Revisar al abrir videoprep** (default on). **Revisar cada N horas** while open.
- **Tarea programada**: "Configurar..." creates/removes a Windows scheduled task running `videoprep.exe --check`
  (`schtasks /Create /SC HOURLY|DAILY /TN videoprep-check ...`, current user, only when logged on, no admin needed).
  On macOS later use a launchd agent. Show exactly what will be created before creating it and offer "Quitar tarea".
- Only one check/download run at a time (lock file) so the UI and the scheduled task do not collide.

UI details (Suscripciones)
- Table: On checkbox, Canal, Que baja, Filtros (summary text), Formato, Ultima vez, Nuevos. Buttons: Agregar, Editar, Quitar,
  Revisar ahora. Add/Edit opens a dialog with the same fields as Explorar plus folder, format, first-run choice.
- Row shows `lastError` in red with a tooltip. Disabled rows are grey.
- Warning text (settings or first use): many requests while logged in can put the account at risk; prefer a secondary
  account or no cookies for public channels; videoprep spaces requests but cannot guarantee anything.

## 3. Other backlog items folded into 0.2.0
- Delete key removes selected queue rows (no confirmation; keep a sane selection afterwards).
- Preview robustness for other sites (AUDIT M11).
- Fixes from the owner's real testing: ask for the Log tab output; add a "Copiar diagnostico" button (versions, tools, browsers,
  last 200 log lines, no cookies/URLs with tokens).

## 4. i18n keys to add (es / en)
tab_scraping = Scraping; sc_explore = Explorar / Explore; sc_subs = Suscripciones / Subscriptions; sc_search = Buscar videos / Find videos;
sc_tabs_videos/shorts/streams/playlists = Videos / Shorts / Directos / Listas de reproducción; sc_from = Publicados desde / Published from;
sc_to = hasta / to; sc_dur_min/max = Duración mín. / máx.; sc_incl = El título incluye / Title includes; sc_excl = excluye / excludes;
sc_max = Cantidad máx.; sc_order_new/old = Más nuevos primero / Más viejos primero; sc_skip_live = Omitir directos en vivo;
sc_skip_members = Omitir exclusivos de miembros; sc_skip_done = Omitir los ya descargados; sc_apply = Aplicar filtros;
sc_all/none/invert = Todo / Nada / Invertir; sc_selected = {0} de {1} seleccionados; sc_to_queue = Enviar a la cola;
sc_subscribe = Suscribirme a esto...; sc_status_new/done/queued/live/members; sc_approx_note; sc_check_now = Revisar ahora;
sc_check_start = Revisar al abrir videoprep; sc_check_every = Revisar cada {0} horas mientras esté abierto;
sc_task = Revisar aunque videoprep esté cerrado (tarea programada de Windows); sc_task_config = Configurar...; sc_task_remove = Quitar tarea;
sc_firstrun_new = Bajar solo lo nuevo desde ahora; sc_firstrun_backfill = Bajar también lo anterior que cumpla los filtros;
sc_warn_account = aviso de riesgo de cuenta; sc_legal = aviso legal; sc_blocked = YouTube pidió verificación: se frenó la revisión.
Anglicism-free Spanish; keep "Scraping" only as the tab name the owner chose.

## 5. Acceptance criteria
1. Explorar lists a public channel's Videos tab, filters locally (date, duration, title include/exclude, count, order) without new requests, and sends the checked rows to the queue.
2. A subscription created from Explorar survives restart, runs "Revisar ahora", downloads only new videos, and a second run downloads nothing.
3. `videoprep --check` works with the window closed, respects the lock, logs to file, returns the documented exit codes.
4. Rate-limit/bot-check stops the run cleanly with a clear message; nothing loops.
5. All new strings exist in es and en; UI resizable; works in all 6 themes.
6. Unit tests for filters, archive handling, first-run logic and subscription JSON round-trip; integration test (opt-in) for flat listing parse using a saved fixture.
