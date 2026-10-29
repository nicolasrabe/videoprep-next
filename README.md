# videoprep

**[English](README.en.md)**

Herramienta gratuita para editores de video: pegás un enlace (YouTube y cualquier sitio que soporte yt-dlp), elegís
formato, fragmentos y nombres, y bajás archivos listos para editar en Premiere Pro, DaVinci Resolve o el programa que uses.

Versión actual: **0.3.0** (versión de prueba, para Windows). Es un solo `videoprep.exe`, no hace falta instalar nada.

## Qué hace
- **Descargar**: MP4 (H.264 + AAC), MOV (H.264 o ProRes 422 HQ), WebM, MKV, o solo audio (M4A, MP3, WAV, FLAC, Opus),
  con tope de resolución. Vista previa con título, duración y calidad disponible.
- **Fragmentos** con código de tiempo `HH:MM:SS:FF` (varios por video). El fotograma final se incluye; cada
  fragmento se revisa con ffprobe antes de guardarlo. `-00:00:00:15` = los últimos 15 fotogramas.
- **Nombres**: título original, título personalizado o serie (nombre + número, sigue después de lo que ya hay en la carpeta).
- **Cola**: varios enlaces a la vez, editar, mover, quitar (tecla Supr), reintentar los que fallaron. Se guarda al cerrar.
  Podés **cambiar el nombre** de una fila (doble clic o F2), **cambiar varios elementos a la vez** (formato, resolución, nombres),
  duplicar, **deshacer** (Ctrl+Z), volver a descargar y abrir cada fila para ver miniatura, título, canal, fecha y descripción.
- **Cargar archivo con enlaces** (`.txt`, `.docx`, `.md`, `.html`, `.csv`, o arrastrándolo): videoprep encuentra los enlaces, te los
  muestra para revisar (repetidos, ya en la cola, que no son videos) y los agrega a la cola. El archivo solo se lee.
- **Perfiles de formato**: guardá el formato, la resolución y los nombres con un nombre y usalos en Descargar, en la cola y en Scraping.
- **Velocidad constante** (casilla, apagada por defecto): los videos de velocidad variable (Twitch, X, capturas de pantalla) dan
  problemas en Premiere. Esta opción solo toca los que **de verdad** son de velocidad variable y los convierte a velocidad constante,
  en el mismo paso que el corte; antes de reemplazar nada comprueba fotogramas, duración y audio. Si algo no cierra, el original queda intacto.
- **Modo mini**: una sola línea con el progreso, que no le quita el teclado al programa donde estás escribiendo.
- **Enlaces copiados** (apagado por defecto): agrega a la cola los enlaces de video que copiás, solo mientras la opción esté activada.
- **Lista de fuentes** (apagada por defecto): al terminar cada tanda guarda un `.csv` o `.txt` con título, canal, enlace y nombre del archivo.
- **Actualizaciones**: avisa cuando hay una versión nueva y la instala si tocás **Actualizar** (ver más abajo).
- **Scraping → Explorar**: pegás un canal o una lista, ves todos sus videos en una tabla, filtrás por fecha, duración,
  palabras del título y cantidad, y mandás los que marques a la cola.
- **Scraping → Suscripciones**: videoprep revisa los canales que elijas y baja solo lo nuevo, nunca dos veces lo mismo.
  Puede revisar al abrirse, cada N horas, o con videoprep cerrado (tarea programada de Windows).
- Español e inglés, seis temas de color, todas las columnas de las tablas se pueden agrandar.

## Descargar y abrir
1. En [Releases](https://github.com/nicolasrabe/videoprep-next/releases) bajá `videoprep-v0.3.0-win-x64.zip` y
   descomprimilo en una carpeta tuya (por ejemplo `Documentos\videoprep`).
2. Abrí `videoprep.exe`. Como el programa no está firmado, Windows puede mostrar "Windows protegió su PC":
   hacé clic en **Más información** y después en **Ejecutar de todas formas**.
3. La primera vez que apretás **Descargar**, videoprep baja las herramientas que necesita (yt-dlp, FFmpeg y Deno,
   unos 200 MB, una sola vez) a `%LOCALAPPDATA%\videoprep\tools`. En **Ajustes** podés actualizar yt-dlp cuando
   quieras (**Actualizar herramientas**) o activar "Buscar actualizaciones de yt-dlp automáticamente (una vez por semana)".

> **Si ya tenés la 0.2.0**: esa versión no sabe actualizarse sola. Bajá la 0.3.0 a mano una vez (paso 1) y reemplazá el
> `videoprep.exe` viejo por el nuevo. Desde la 0.3.0 en adelante, videoprep te avisa cuando hay una versión nueva.

Si querés comprobar que el zip no fue modificado, compará su SHA-256 con el de `SHA256SUMS.txt` en la misma página:
en PowerShell, `Get-FileHash videoprep-v0.3.0-win-x64.zip`.

## Actualizaciones
- Al abrirse (como máximo una vez por día) videoprep mira la lista pública de versiones de GitHub; en **Ajustes** podés apagarlo y
  tocar **Buscar actualizaciones ahora**. No manda nada tuyo (solo el pedido a GitHub, que lleva el número de versión de videoprep).
- Si hay una versión más nueva aparece una franja arriba: **Ver novedades** o **Actualizar**. Nada se instala sin que toques
  **Actualizar**, y no hace nada mientras hay una descarga en curso.
- **Actualizar** baja el zip y `SHA256SUMS.txt` de esa versión, comprueba el SHA-256 y, si coincide, reemplaza el programa y lo abre
  de nuevo (la cola y los ajustes se conservan). Si falta la comprobación o no coincide, no instala nada y te ofrece abrir la página.
- La versión anterior queda al lado como `videoprep.exe.old`. **Para volver atrás**: cerrá videoprep, borrá `videoprep.exe` y
  renombrá `videoprep.exe.old` a `videoprep.exe`. Si la versión nueva se cierra sola al abrirse, videoprep vuelve a la anterior solo.
- Si la carpeta de `videoprep.exe` no deja escribir (por ejemplo `Archivos de programa`), lo explica antes de bajar nada y no pide
  permisos de administrador: mové videoprep a una carpeta tuya (como Documentos) o bajá la versión nueva a mano.
- Qué protege el SHA-256: contra una descarga dañada o un zip cambiado. **No** protege si alguien controlara el repositorio de GitHub
  (podría cambiar el zip y la suma a la vez); la protección completa sería firmar el programa, algo que queda para más adelante.
  Por eso Windows sigue mostrando "Windows protegió su PC" la primera vez.
- En Mac y Linux el botón solo abre la página de descargas.

## Sitios
YouTube (videos, cortos, directos, listas), Twitch, Kick, Vimeo, X, Instagram, Facebook, TikTok y
[cualquier sitio que soporte yt-dlp](https://github.com/yt-dlp/yt-dlp/blob/master/supportedsites.md).
En **Explorar** y **Suscripciones** funcionan canales y listas de YouTube y perfiles de Twitch, Kick y Vimeo.
Los perfiles de Instagram, X y TikTok hoy no se pueden listar (yt-dlp no lo permite); sus publicaciones sueltas sí se bajan.

Algunos videos piden iniciar sesión. En **Ajustes** elegís de qué navegador leer las cookies (las lee yt-dlp en tu PC,
no se mandan a ningún lado) o un archivo `cookies.txt`. Para canales públicos conviene no usar cookies: muchos pedidos
con tu sesión iniciada pueden poner tu cuenta en riesgo.

## Revisión automática sin abrir la ventana
`videoprep.exe --check` revisa las suscripciones activas y termina. Es lo que usa la tarea programada que crea el botón
**Configurar...** (te muestra el comando exacto antes de crearla; **Quitar tarea** la borra). Deja un registro en
`%LOCALAPPDATA%\videoprep\logs\check-AAAAMMDD.log` y termina con código 0 (bien), 1 (algo falló), 2 (el sitio pidió
verificación y se frenó) o 3 (había otra descarga o revisión en curso).

## Dónde guarda las cosas
- `%APPDATA%\videoprep`: ajustes, cola, suscripciones y el historial de lo descargado.
- `%LOCALAPPDATA%\videoprep`: herramientas, registros y las actualizaciones que se están instalando.

Para desinstalar: borrá la carpeta de `videoprep.exe`, esas dos carpetas y, si la creaste, la tarea programada
(**Quitar tarea**).

## Si algo falla
Andá a la pestaña **Registro**, apretá **Copiar diagnóstico** y pegalo en un
[issue](https://github.com/nicolasrabe/videoprep-next/issues). El diagnóstico no incluye cookies.

## Para desarrolladores
Hace falta el SDK de .NET 10. `dotnet build videoprep.slnx`, `dotnet test videoprep.slnx`, y para el exe:
`dotnet publish src/Videoprep.App -c Release -r win-x64 -o dist/win-x64`. La versión vive en `Directory.Build.props`.
La versión anterior (0.1.1, PowerShell) está en `legacy-ps/`.

## Aviso legal
Usalo solo con contenido propio, con licencia o con permiso para usarlo. videoprep no está afiliado a YouTube, Google,
Twitch, Kick, X, Meta ni TikTok. No envía datos a ningún lado: las cookies de sesión las lee yt-dlp en tu PC.

Licencia MIT.
