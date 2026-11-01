# Mapa del código (leer esto antes de abrir archivos)

Dos proyectos: **Videoprep.Core** (motor, sin interfaz, sin referencias a Windows fuera de lo necesario) y **Videoprep.App**
(Avalonia: ventanas y MVVM). La interfaz llama al motor; el motor informa por eventos / `IProgress`.

## Videoprep.Core (`src/Videoprep.Core`)
| Carpeta | Para qué sirve (clases principales) |
|---|---|
| `Engine/` | Descarga y corte. `JobEngine` ejecuta un trabajo; `YtDlpArgs`/`FfmpegArgs` arman los argumentos; `ProcessRunner` lanza programas; `OutputVerifier` revisa el archivo con ffprobe; `ConstantRate` pasa a fotogramas constantes; `CookieStrategy` elige las cookies. |
| `Tools/` | `ToolManager`: descarga y mantiene yt-dlp, ffmpeg y deno. |
| `Media/` | `CutPlanner` (cortes exactos), `MediaInfo` (lee ffprobe), `VfrDetector`, `SourceTiming`. |
| `Models/` | `QueueItem` (con fragmentos que llevan nombre), `Job`, `JobPlanner` (de la cola a trabajos), `QueueEdits` (edición en lote), `MiniPlacement` (dónde van la ficha y la barra mini, por monitor y escala). |
| `Storage/` | `AppPaths` (carpetas), `Settings`/`SettingsStore`, `QueueStore`, `FileNames`, `RunLock`. |
| `Import/` | Lee enlaces de .txt/.docx/.md/.html/.csv: `LinkFileReader`, `DocxLinks`, `LinkClassifier`, `LinkNormalizer`, `ImportPlanner`. |
| `Scraping/` | `ScrapeSearch` (la búsqueda: un listado, corte temprano, fechas estrictas, caché, topes de 50 y 150), `SourceLister` (pedido liviano, listado, fecha exacta), `Filters` (regla de fechas), `Subscription`, `CheckRunner` (revisión: usa `ScrapeSearch` y corta al llegar a lo ya registrado), `ArchiveStore`. |
| `Update/` | Autoactualización: `UpdateClient` (GitHub), `ReleaseFeed`, `UpdateStager`, `UpdateApplier`, `SemVer`. |
| `Preview/` | `PreviewService`, `VideoInfoService`: datos y miniatura de un enlace. |
| `Profiles/` | Perfiles de formato: `FormatProfile`, `ProfileStore`. |
| `Formats/`, `Text/`, `Sources/` | `FormatCatalog`; `UrlParser`, `Timecode`, `SafeName`; `SourceList`. |
| `I18n/`, `Themes/`, `Logging/` | `Localizer` (es/en); `Palette` (6 temas); `Log`, `Diagnostics`. |

## Videoprep.App (`src/Videoprep.App`)
| Carpeta | Para qué sirve |
|---|---|
| `ViewModels/` | `MainViewModel` (partido en `.Download`, `.Queue`, `.Run`, `.Scraping`, `.Settings`, `.Update`, `.Mini`, `.Profiles`, `.Extras`), más `ExploreViewModel`, `SubscriptionsViewModel`, `BatchEditViewModel`, `ImportReviewViewModel`, `OptionsViewModel`. |
| `Views/` | Pantallas `.axaml`: `MainWindow` (pestañas con ícono, botón Modo mini), `DownloadView` (Prep), `QueueView`, `ScrapingView` (tres pasos), `VideoCard` (la ficha de fila abierta, de la Cola y de Scraping), `MiniWindow` (ficha y barra), `Dialogs/`, `SimpleViews/`. |
| `Services/` | `Platform` (todo lo que depende del sistema), `MiniPlacer` (niveles maxi/mini/micro y su posición; `IScreenSource` e `IMiniSurfaces` para probarlo sin ventanas), `AppServices`, `ThemeService`, `Loc`/`TExtension` (textos), `ScheduledTask`, `ClipboardWatcher`. |
| `Cli/` | Comandos de consola: `CheckCommand`, `DemoCommand`, `ConsoleHost`. |

## Pruebas (`tests/`)
`Videoprep.Core.Tests` (motor; las `*Integration*` usan ffmpeg real, categoría `Integration`), `Videoprep.App.Tests`
(pantallas sin ventana real, guardan capturas), `FakeYtDlp` (yt-dlp falso), `desktop/` (scripts a mano en un escritorio real).
Rápido: `powershell -File scripts/check.ps1`. Las pruebas con `[Trait("Inestable","si")]` no corren en el CI normal. `TextFilesTests` revisa los textos y las pantallas leyendo los archivos (sin Avalonia). Capturas de cierre: `--filter "Capturas=si"`.
