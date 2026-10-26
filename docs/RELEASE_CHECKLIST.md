# Release checklist

## Developer (every release)
1. All phase branches merged into `main` through a pull request; CI green on Windows, macOS and Ubuntu (and the
   integration job with real ffmpeg).
2. Version only in `Directory.Build.props`: `VersionPrefix` = the release, `VersionSuffix` empty. CHANGELOG heading
   `## X.Y.Z - YYYY-MM-DD`; README es/en mention the same version and zip name.
3. Local build: `dotnet publish src/Videoprep.App -c Release -r win-x64 -o dist/win-x64`, then open
   `dist/win-x64/videoprep.exe` once (window opens, About shows the version) and run
   `dist/win-x64/videoprep.exe --check` (exit code 0 or 3).
4. Tag on `main`: `git tag vX.Y.Z` and `git push origin vX.Y.Z`. `release.yml` runs the tests, builds
   `videoprep-vX.Y.Z-win-x64.zip`, writes `SHA256SUMS.txt` and creates the GitHub **pre-release** (0.x is always pre-release).
5. Download the zip from the release page, compare `Get-FileHash` with `SHA256SUMS.txt`, unzip, open it once.
6. Give the owner the link and the test list below; ask for **Registro → Copiar diagnóstico** if anything fails.

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
