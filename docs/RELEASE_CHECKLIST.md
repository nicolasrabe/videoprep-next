# Release checklist

## Developer (every release)
1. All phase branches merged into `main` through a pull request; CI green on Windows, macOS and Ubuntu (and the
   integration job with real ffmpeg).
2. Version only in `Directory.Build.props`: `VersionPrefix` = the release, `VersionSuffix` empty. CHANGELOG heading
   `## X.Y.Z - YYYY-MM-DD`; README es/en mention the same version and zip name; `docs/RELEASE_NOTES_X.Y.Z.md` exists
   (`release.yml` publishes it as the release text; without it the CHANGELOG is used).
3. Local build: `dotnet publish src/Videoprep.App -c Release -r win-x64 -o dist/win-x64`, then open
   `dist/win-x64/videoprep.exe` once (window opens, About shows the version) and run
   `dist/win-x64/videoprep.exe --check` (exit code 0 or 3).
4. Tag on `main`, only when the owner says "publicá": `git tag vX.Y.Z` and `git push origin vX.Y.Z`. `release.yml` runs the tests,
   builds `videoprep-vX.Y.Z-win-x64.zip` (it must hold `videoprep.exe` and nothing else: the updater takes only that file), writes
   `SHA256SUMS.txt` with the zips only, checks it with `sha256sum -c` and creates the GitHub release, marked **pre-release** for any 0.x
   or `-suffix` version. The updater of the installed programs reads this release: the names `videoprep-<tag>-win-x64.zip` and
   `SHA256SUMS.txt` must not change.
5. Download the zip from the release page, compare `Get-FileHash` with `SHA256SUMS.txt`, unzip, open it once.
6. **The updater against a real release** cannot be tried until a release newer than the installed one exists: after the next
   release (for example 0.3.1), open the previous one, wait for the banner (or Ajustes → Buscar actualizaciones ahora), press
   Actualizar and check: the new version opens, `videoprep.exe.old` is next to it, the queue is still there. Record the result in `docs/VERIFY.md`.
7. Give the owner the link and the test list below; ask for **Registro → Copiar diagnóstico** if anything fails.
8. Say it plainly on the release page (the notes do): people with 0.2.0 have no updater and must download 0.3.0 by hand once.

## Prueba del dueño (en Windows, con el zip publicado)
Marcá cada punto. Si algo falla: pestaña **Registro → Copiar diagnóstico** y mandalo.

**Primer uso**
- [ ] Abrir `videoprep.exe` (si aparece "Windows protegió su PC": Más información → Ejecutar de todas formas).
- [ ] Pegar un enlace de YouTube y apretar **Descargar**: pregunta si baja las herramientas, las baja y sigue.

**Descargar**
- [ ] Enlace completo, enlace corto (`youtu.be/...`) y solo el ID: los tres se reconocen y muestran la vista previa.
- [ ] Fragmentos: `00:00:10:00` a `00:00:12:00`, y `-00:00:00:15` (últimos 15 fotogramas). Se abren bien en Premiere.
- [ ] Formatos: MP4, MOV ProRes y un audio (WAV). Nombres: título original, personalizado y serie.
- [ ] Un título con símbolos (`:` `?` o emoji): se baja y NO dice "SKIPPED".
- [ ] Pegar un segundo enlace: el primero pasa solo a la cola.
- [ ] Enlaces de Twitch, X e Instagram (una publicación suelta).
- [ ] **Detener** a mitad de una descarga: se frena sin colgarse.

**Cola**
- [ ] Editar, subir/bajar, quitar con la tecla Supr, reintentar los que fallaron.
- [ ] Cerrar y abrir videoprep: la cola sigue ahí.

**Scraping**
- [ ] Explorar un canal: la tabla se llena, los filtros se aplican al toque, **Enviar a la cola** manda los marcados.
- [ ] Pegar un perfil de Instagram en Explorar: explica que no se puede y qué hacer.
- [ ] **Suscribirme a esto...** con "Bajar también lo anterior" y Cantidad máx. 2: **Revisar todas** baja 2;
      revisar de nuevo: "no hay videos nuevos".
- [ ] **Configurar...** la tarea programada, `schtasks /Run /TN videoprep-check` en una terminal, y después **Quitar tarea**.

**General**
- [ ] Cambiar idioma (Ajustes) y los seis temas: todo se lee bien.
- [ ] Agrandar la ventana y las columnas de las tablas.
- [ ] Registro → **Probar la instalación**: dice qué herramientas encontró y sus versiones.
- [ ] Ajustes → **Actualizar herramientas**: actualiza yt-dlp sin errores.

## 0.3.0: lo nuevo (en Windows, con el zip publicado)
**Cola y archivos**
- [ ] **Cargar archivo...** con un `.txt` o `.docx` con enlaces: aparece la ventana de revisión, se agregan a la cola.
- [ ] Doble clic en un nombre de la cola (o F2) lo cambia; **Cambiar N elementos** con varias filas marcadas; **Ctrl+Z** deshace.
- [ ] Abrir una fila con la flecha: miniatura, título, canal y fecha.

**Mini, portapapeles, perfiles, fuentes**
- [ ] **Mini**: se achica a una línea, mientras escribís en otro programa no te saca el teclado; clic derecho → Cerrar modo mini.
- [ ] Ajustes → "agregar a la cola los enlaces que copio": copiar un enlace de YouTube lo suma; apagarlo y copiar otro: no pasa nada.
- [ ] **Guardar como perfil...** y elegirlo en otra pantalla (Descargar, Cambiar N elementos, Scraping).
- [ ] Ajustes → lista de fuentes: al terminar una tanda aparece el `.csv` en la carpeta de destino y se abre bien en Excel.

**Velocidad constante**
- [ ] Un video de velocidad variable (Twitch, X o una grabación de pantalla) con la casilla **Velocidad constante**: sale a velocidad constante, el audio acompaña, y se abre bien en Premiere.
- [ ] Un video que ya es de velocidad constante con la casilla marcada: el Registro dice "velocidad constante ... se deja como está" y el archivo no se vuelve a codificar.

**Actualizaciones**
- [ ] Ajustes → **Buscar actualizaciones ahora**: dice "Ya tenés la última versión (0.3.0)" (o avisa si ya hay otra).
- [ ] Con una versión posterior publicada (ver el punto 6 de arriba): la franja "Hay una versión nueva", **Ver novedades** abre la página, **Actualizar** reemplaza y abre la nueva, y queda `videoprep.exe.old`.
- [ ] Apagar "Buscar una versión nueva al abrir videoprep": al abrir no se busca nada.
