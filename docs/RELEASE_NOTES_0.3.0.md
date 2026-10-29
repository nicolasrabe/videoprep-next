# videoprep 0.3.0 (versión de prueba / test release, Windows)

## Español

**Si tenés la 0.2.0, leé esto primero:** la 0.2.0 no sabe actualizarse sola. Bajá `videoprep-v0.3.0-win-x64.zip` de abajo,
descomprimilo y reemplazá tu `videoprep.exe` viejo por el nuevo (tus ajustes, tu cola y tus suscripciones se conservan).
Es la única vez que hay que hacerlo a mano: desde la 0.3.0, videoprep avisa cuando hay una versión nueva y la instala si tocás **Actualizar**.

### Novedades
- **Cargar archivo con enlaces** (`.txt`, `.docx`, `.md`, `.html`, `.csv`, o arrastrándolo): encuentra los enlaces, los muestra para revisar y los agrega a la cola.
- **Cola editable**: cambiar el nombre de una fila, cambiar varios elementos a la vez, duplicar, deshacer (Ctrl+Z), volver a descargar, abrir cada fila para ver miniatura, título, canal y descripción.
- **Modo mini**: una sola línea con el progreso, siempre encima si querés, sin quitarle el teclado al programa donde escribís.
- **Enlaces copiados** (apagado por defecto): agrega a la cola los enlaces de video que copiás, solo mientras la opción esté activada.
- **Perfiles de formato**: guardá formato, resolución y nombres con un nombre y usalos en Descargar, en la cola y en Scraping.
- **Lista de fuentes** (apagada por defecto): al terminar cada tanda guarda un `.csv` o `.txt` con título, canal, enlace y nombre del archivo.
- **Velocidad constante** (apagada por defecto): convierte a velocidad constante los videos de velocidad variable (Twitch, X, capturas de pantalla) para que Premiere los lea bien. Solo toca los que de verdad son variables; comprueba fotogramas, duración y audio antes de reemplazar nada, y si algo falla el original queda intacto.
- **Actualizaciones de videoprep**: una franja avisa cuando hay una versión nueva; **Actualizar** comprueba el SHA-256 del zip, reemplaza el programa y deja la versión anterior como `videoprep.exe.old` para volver atrás. Se puede apagar y hay un botón "Buscar actualizaciones ahora" en Ajustes.

### Cosas que conviene saber
- El programa no está firmado: la primera vez Windows puede mostrar "Windows protegió su PC" (Más información → Ejecutar de todas formas).
- El SHA-256 protege contra descargas dañadas, no contra alguien que controle el repositorio. Firmar el programa queda para más adelante.
- Si la carpeta de `videoprep.exe` no deja escribir (por ejemplo `Archivos de programa`), la actualización automática lo explica y no pide permisos de administrador.
- En Mac y Linux el botón de actualizar solo abre la página de descargas.
- Para comprobar el zip: `Get-FileHash videoprep-v0.3.0-win-x64.zip` en PowerShell y compará con `SHA256SUMS.txt`.
- No hace falta instalar nada. Usalo solo con contenido propio, con licencia o con permiso para usarlo.

## English

**If you have 0.2.0, read this first:** 0.2.0 cannot update itself. Download `videoprep-v0.3.0-win-x64.zip` below, unzip it and replace your
old `videoprep.exe` with the new one (your settings, queue and subscriptions are kept). It is the only time it has to be done by hand: from
0.3.0 on, videoprep tells you when a new version exists and installs it when you press **Update**.

### What's new
- **Load a file with links** (`.txt`, `.docx`, `.md`, `.html`, `.csv`, or drag it in): finds the links, shows them for review and adds them to the queue.
- **Editable queue**: rename a row, change several items at once, duplicate, undo (Ctrl+Z), download again, open each row to see thumbnail, title, channel and description.
- **Mini mode**: one line with the progress, always on top if you want, without taking the keyboard from the program you type in.
- **Copied links** (off by default): adds the video links you copy to the queue, only while the option is on.
- **Format profiles**: save format, resolution and names under a name and use them in Download, the queue and Scraping.
- **List of sources** (off by default): when a batch ends it saves a `.csv` or `.txt` with title, channel, link and file name.
- **Constant frame rate** (off by default): makes variable-frame-rate videos (Twitch, X, screen recordings) constant so Premiere reads them well. It only touches the ones that really are variable; it checks frames, length and audio before replacing anything, and if a check fails the original stays untouched.
- **videoprep updates**: a banner tells you when a new version exists; **Update** checks the zip's SHA-256, replaces the program and keeps the previous version as `videoprep.exe.old` so you can go back. It can be turned off and there is a "Check for updates now" button in Settings.

### Good to know
- The program is not signed: the first time Windows may show "Windows protected your PC" (More info → Run anyway).
- The SHA-256 protects against damaged downloads, not against someone who controls the repository. Signing the program is for later.
- If the folder of `videoprep.exe` does not allow writing (for example `Program Files`), the automatic update says so and does not ask for administrator rights.
- On Mac and Linux the update button only opens the downloads page.
- To check the zip: `Get-FileHash videoprep-v0.3.0-win-x64.zip` in PowerShell and compare with `SHA256SUMS.txt`.
- Nothing to install. Use it only with content you own, have licensed or have permission to use.
