# miniprep - tanda 1 (feat/miniprep)

Mockup: docs/mockups-miniprep/mockup-prep-y-cola-simple.html (copia del que pidió Nico).

## Estructura
- `src/Videoprep.Controls` (nuevo): Loc, TExtension, LocConverter, LoadingText, AspectBox, TitleBarStyle y WindowTitleBar (DWM), Spinner.axaml.
  Salieron de Videoprep.App sin cambiar su comportamiento (se usan con `{c:T clave}`; las pruebas de textos ahora aceptan `{s:T}` y `{c:T}`).
- `src/Miniprep.App` (nuevo): Logic (MiniQueue, QueueEntry, RowMenu, Names, Display), Services (MiniPaths, MiniSettings, MiniServices,
  Downloader sobre JobEngine, ToolsGate, RunGate, IMiniUi), ViewModels (Main, Prep, Queue), Views (ventana, Prep, Cola).
- Textos nuevos `mp_*` en es.json / en.json del Core (misma tabla de textos que videoprep).
- Datos en %LOCALAPPDATA%\miniprep (settings.json, queue.json, logs, cache); herramientas: carpeta de videoprep si ya existe, si no la propia.
- Salida: dist\miniprep-win-x64\miniprep.exe (single-file, autocontenido, ícono assets/videoprep.ico). CI: build y pruebas ya cubren Miniprep
  (están en videoprep.slnx) y el job publish genera también miniprep.exe.

## Decisiones
- "Mejor disponible" = hasta 1080p H.264 (lo que acepta Premiere); 1080p/720p/480p ponen el tope. MP3: ffmpeg del motor.
- Cookies del navegador: nunca por defecto; solo al apretar "Reintentar con las cookies de tu navegador" (por link, esa sesión).
- Una descarga a la vez (Prep o cola). Si el archivo ya existe el motor agrega _2 (nunca pisa).
- Fila con error vuelve a "En cola" al apretar "Iniciar cola". Datos faltantes: de a uno, una sola vez por sesión por fila, espera si Prep lee un enlace.
- Lista/canal: solo el aviso (sin integración con videoprep).

## Pruebas
- Miniprep.App.Tests: 53 normales (lógica, flujos con yt-dlp falso, pantallas con capturas) + 1 de integración (real: datos + MP4 + MP3).
- No verificado: otros sitios además de YouTube; el menú de clic derecho y el arrastre con el mouse real (solo la lógica detrás);
  el diálogo "Guardar como" nativo (solo con un falso).
