# Mapa del código (leer esto antes de abrir archivos)

Dos proyectos: **Videoprep.Core** (motor, sin interfaz, sin referencias a Windows fuera de lo necesario) y **Videoprep.App**
(Avalonia: ventanas y MVVM). La interfaz llama al motor; el motor informa por eventos / `IProgress`.

## Videoprep.Core (`src/Videoprep.Core`)
| Carpeta | Para qué sirve (clases principales) |
|---|---|
| `Engine/` | Descarga y corte. `JobEngine` ejecuta un trabajo; `YtDlpArgs`/`FfmpegArgs` arman los argumentos; `ProcessRunner` lanza programas; `OutputVerifier` revisa el archivo con ffprobe; `ConstantRate` pasa a fotogramas constantes; `CookieStrategy` elige las cookies. |
| `Tools/` | `ToolManager`: descarga y mantiene yt-dlp, ffmpeg y deno. |
| `Media/` | `CutPlanner` (cortes exactos), `MediaInfo` (lee ffprobe), `VfrDetector`, `SourceTiming`. |
| `Models/` | `QueueItem` (con fragmentos que llevan nombre), `Job`, `JobPlanner` (de la cola a trabajos), `QueueEdits` (edición en lote), `MiniPlacement` (dónde van la ficha, la barra mini y la ventana completa al volver, por monitor y escala), `QueueOrder` (reordenar la cola arrastrando). |
| `Storage/` | `AppPaths` (carpetas), `Settings`/`SettingsStore`, `QueueStore`, `FileNames`, `RunLock`. |
| `Import/` | Lee enlaces de .txt/.docx/.md/.html/.csv: `LinkFileReader`, `DocxLinks`, `LinkClassifier`, `LinkNormalizer`, `ImportPlanner`. |
| `Scraping/` | `ScrapeSearch` (la búsqueda: un listado, corte temprano, fechas estrictas, caché, topes de 50 y 150), `SourceLister` (pedido liviano, listado, fecha exacta), `Filters` (regla de fechas), `Subscription`, `CheckRunner` (revisión: usa `ScrapeSearch` y corta al llegar a lo ya registrado), `ArchiveStore`. |
| `Update/` | Autoactualización: `UpdateClient` (GitHub), `ReleaseFeed`, `UpdateStager`, `UpdateApplier`, `SemVer`. |
| `Preview/` | `PreviewService` (vista previa de Prep), `VideoInfoService` (datos de la ficha: caché en disco de 24 h, miniatura en paralelo, prefetch de la fila siguiente), `ThumbnailService` (la mejor miniatura, caché en memoria y disco de 200 MB), `ThumbnailExporter` (guardar como JPG/PNG). |
| `Profiles/` | Perfiles de formato: `FormatProfile`, `ProfileStore`. |
| `Formats/`, `Text/`, `Sources/` | `FormatCatalog`; `UrlParser`, `Timecode`, `SafeName`; `SourceList`. |
| `I18n/`, `Themes/`, `Logging/` | `Localizer` (es/en); `Palette` (6 temas); `Log`, `Diagnostics`. |

## Videoprep.App (`src/Videoprep.App`)
| Carpeta | Para qué sirve |
|---|---|
| `ViewModels/` | `MainViewModel` (partido en `.Download`, `.Queue`, `.Run`, `.Scraping`, `.Settings`, `.Update`, `.Mini`, `.Profiles`, `.Extras`, `.QueueBar` (barra de la Cola y reordenar), `.Thumbs` (Descargar miniatura)), más `ExploreViewModel`, `SubscriptionsViewModel`, `BatchEditViewModel`, `ImportReviewViewModel`, `OptionsViewModel`. |
| `Views/` | Pantallas `.axaml`: `MainWindow` (pestañas con ícono, botón Modo mini), `DownloadView` (Prep), `QueueView`, `ScrapingView` (tres pasos), `VideoCard` (la ficha de fila abierta, de la Cola y de Scraping), `CollapsibleSection` y `LoadingText` (componentes compartidos: tarjeta plegable y texto de carga), `MiniWindow` (ficha y barra), `Dialogs/`, `SimpleViews/`. |
| `Services/` | `Platform` (todo lo que depende del sistema), `MiniPlacer` (niveles maxi/mini/micro y su posición; `IScreenSource` e `IMiniSurfaces` para probarlo sin ventanas), `AppServices`, `ThemeService`, `Loc`/`TExtension` (textos), `ScheduledTask`, `ClipboardWatcher`. |
| `Cli/` | Comandos de consola: `CheckCommand`, `DemoCommand`, `ConsoleHost`. |

## Pruebas (`tests/`)
`Videoprep.Core.Tests` (motor; las `*Integration*` usan ffmpeg real, categoría `Integration`), `Videoprep.App.Tests`
(pantallas sin ventana real, guardan capturas), `FakeYtDlp` (yt-dlp falso), `desktop/` (scripts a mano en un escritorio real).
Rápido: `powershell -File scripts/check.ps1`. Las pruebas con `[Trait("Inestable","si")]` no corren en el CI normal. `TextFilesTests` revisa los textos y las pantallas leyendo los archivos (sin Avalonia). Capturas de cierre: `--filter "Capturas=si"`.

## Videoprep.Controls (`src/Videoprep.Controls`) - compartido por videoprep y miniprep
`Loc` / `TExtension` (`{c:T clave}`) / `LocConverter` (textos es/en), `LoadingText` (spinner "Cargando...") y `Spinner.axaml`, `AspectBox` (caja 16:9), `TitleBarStyle` + `WindowTitleBar` (barra de título según el tema, DWM).

## Miniprep.App (`src/Miniprep.App`) - la app chica (Prep + Cola), exe aparte `miniprep.exe`
| Carpeta | Para qué sirve |
|---|---|
| `Logic/` | Sin pantallas: `MiniQueue` (cola, orden, guardado en queue.json), `QueueEntry`, `RowMenu` (menú de clic derecho como datos), `Names` (nombres de "Guardar como"), `Display` (textos de fechas, vistas, filas). |
| `Services/` | `MiniPaths` (datos en %LOCALAPPDATA%\miniprep; herramientas de videoprep si existen), `MiniSettings`, `MiniServices`, `Downloader` (JobEngine, un trabajo por vez), `ToolsGate`, `RunGate`, `IMiniUi`. |
| `ViewModels/` | `MiniMainViewModel` (pestañas, número de Cola, avisos), `PrepViewModel`, `QueueViewModel` (+ fila). |
| `Views/` | `MainWindow` (implementa `IMiniUi`), `PrepView`, `QueueView` (clic, ✕, arrastre y menú se manejan acá). |
Pruebas: `tests/Miniprep.App.Tests` (lógica, flujos con yt-dlp y descargador falsos, capturas; `RealDownloadTests` = integración real). Mockup: `docs/mockups-miniprep/`.
