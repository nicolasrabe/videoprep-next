using System.Globalization;
using Videoprep.Core;
using Videoprep.Core.Engine;
using Videoprep.Core.Formats;
using Videoprep.Core.Logging;
using Videoprep.Core.Models;
using Videoprep.Core.Storage;
using Videoprep.Core.Text;
using Videoprep.Core.Tools;

namespace Videoprep.App.Cli;

/// <summary>
/// Developer command to watch the engine work without the new screens (docs/DEMO.md):
/// <c>videoprep --demo "&lt;link&gt;" &lt;start&gt; &lt;end&gt; [--carpeta DIR] [--formato mp4] [--herramientas DIR] [--descargar-herramientas]</c>.
/// Downloads one video WITHOUT cookies, cuts one clip, checks it and prints every step in plain Spanish.
/// It never changes settings.json. Exit code: 0 ok, 1 something failed, 2 wrong use or missing tools.
/// </summary>
internal static class DemoCommand
{
    private sealed record Options(string Url, string Start, string End, string Folder, string Format, string? ToolsDir, bool InstallTools);

    private static void Say(string text = "") => Console.WriteLine(text);

    public static async Task<int> RunAsync(string[] args)
    {
        var o = Parse(args);
        if (o is null)
        {
            PrintUsage();
            return 2;
        }

        Say($"videoprep {AppInfo.Version} - modo demostración (prueba el motor sin pantallas)");
        Say("Usa yt-dlp y ffmpeg de verdad. No lee las cookies de ningún navegador.");
        Say();

        var start = Timecode.Parse(o.Start);
        var end = Timecode.Parse(o.End);
        foreach (var (label, tc, text) in new[] { ("inicio", start, o.Start), ("fin", end, o.End) })
        {
            if (!tc.Blank && !tc.Valid)
            {
                Say($"El {label} \"{text}\" no es un código de tiempo válido. Usá HH:MM:SS:FF, por ejemplo 00:01:23:15.");
                return 2;
            }
        }
        var url = UrlParser.LinksFromText(o.Url).FirstOrDefault();
        if (url is null)
        {
            Say($"\"{o.Url}\" no parece un enlace de video.");
            return 2;
        }
        if (!FormatCatalog.IsKnown(o.Format))
        {
            Say($"Formato desconocido: {o.Format}. Opciones: {string.Join(", ", FormatCatalog.All.Select(f => f.Key))}.");
            return 2;
        }

        var paths = AppPaths.ForCurrentUser();
        var log = new Log(paths.LogsDir);

        // ---- Paso 1: herramientas
        Say("Paso 1 de 5 - Busco las herramientas (yt-dlp, ffmpeg, ffprobe y deno)...");
        var toolsDir = o.ToolsDir is null ? paths.ToolsDir : Path.GetFullPath(o.ToolsDir);
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"videoprep/{AppInfo.Version}");
        var tm = new ToolManager(toolsDir, http);
        var tools = tm.Resolve();
        var missing = tools.Missing();
        if (missing.Count > 0)
        {
            Say($"   Faltan: {string.Join(", ", missing)}.");
            if (!o.InstallTools)
            {
                Say($"   Para que las descargue (unos 200 MB, una sola vez) a {toolsDir}, repetí el comando agregando --descargar-herramientas");
                Say("   o indicá dónde están con --herramientas <carpeta>.");
                return 2;
            }
            Say($"   Las descargo a {toolsDir} ...");
            var last = "";
            await tm.InstallAsync(missing, new InlineProgress<ToolProgress>(p =>
            {
                var pct = p.TotalBytes is > 0 ? (int)(p.Bytes * 100 / p.TotalBytes.Value) : -1;
                var line = pct >= 0 ? $"   Descargando {p.Tool}: {pct} %" : $"   Descargando {p.Tool}: {p.Bytes / 1048576} MB";
                if (line != last) { Console.Write("\r" + line + "   "); last = line; }
            }), CancellationToken.None);
            Say();
            tools = tm.Resolve();
            if (tools.Missing().Count > 0)
            {
                Say($"   Siguen faltando: {string.Join(", ", tools.Missing())}.");
                return 2;
            }
        }
        foreach (var (name, path) in new[] { ("yt-dlp", tools.YtDlp), ("ffmpeg", tools.Ffmpeg), ("ffprobe", tools.Ffprobe), ("deno", tools.Deno) })
            Say($"   {name}: {path}");
        Say();

        // ---- Paso 2: pedido
        var fmt = FormatCatalog.Get(o.Format);
        Directory.CreateDirectory(o.Folder);
        Say("Paso 2 de 5 - Preparo el pedido");
        Say($"   Enlace:  {url}");
        Say($"   Fragmento: desde {(start.Blank ? "el principio" : o.Start)} hasta {(end.Blank ? "el final" : o.End)} (el fotograma final se incluye)");
        Say($"   Formato: {fmt.Label}");
        Say($"   Carpeta: {o.Folder}");
        Say();

        var item = new QueueItem { Id = 1, Url = url, Format = fmt.Key, Clips = [new ClipRange(o.Start, o.End)] };
        var jobs = JobPlanner.Build([item], new Dictionary<string, int>());
        var ctx = new EngineContext
        {
            Tools = tools,
            Options = new DownloadOptions(),
            Folder = o.Folder,
            Cookies = [CookieSource.NoCookies],
        };

        var engine = new JobEngine(new ProcessRunner(), log);
        var progressShown = false;
        engine.Event += e =>
        {
            switch (e)
            {
                case StepEvent { Step: EngineStep.Downloading }:
                    Say("Paso 3 de 5 - Descargo el video completo (sin cookies)...");
                    break;
                case ProgressEvent p:
                    var speed = p.Progress.Speed.Length > 0 ? $" a {p.Progress.Speed}" : "";
                    var eta = p.Progress.Eta.Length > 0 ? $", faltan {p.Progress.Eta}" : "";
                    Console.Write(string.Create(CultureInfo.GetCultureInfo("es-AR"), $"\r   {p.Progress.Percent,5:0.0} %{speed}{eta}          "));
                    progressShown = true;
                    break;
                case StepEvent { Step: EngineStep.Processing, Plan: { } plan }:
                    if (progressShown) { Say(); progressShown = false; }
                    Say("Paso 4 de 5 - Corto el fragmento con ffmpeg");
                    Say(string.Create(CultureInfo.GetCultureInfo("es-AR"),
                        $"   Del fotograma {plan.StartFrame} al {plan.EndFrame}: {plan.Frames} fotogramas a {plan.Fps:0.###} por segundo ({plan.Frames / plan.Fps:0.###} s)."));
                    if (plan.Note.Length > 0) Say("   Nota: el fin pedido pasaba el final del video; se usó el último fotograma.");
                    break;
                case StepEvent { Step: EngineStep.Processing }:
                    if (progressShown) { Say(); progressShown = false; }
                    Say("Paso 4 de 5 - Convierto el archivo con ffmpeg...");
                    break;
                case StepEvent { Step: EngineStep.Verifying }:
                    Say("Paso 5 de 5 - Reviso el resultado con ffprobe antes de tocar nada...");
                    break;
                case VerifiedEvent v:
                    var frames = v.Frames is { } f ? $"{f} fotogramas, " : "";
                    var secs = v.Seconds is { } s ? s.ToString("0.###", CultureInfo.GetCultureInfo("es-AR")) + " s" : "duración desconocida";
                    Say(v.Ok ? $"   Bien: tiene {frames}{secs}, como se pidió." : $"   No coincide ({v.Reason}). El video descargado se conserva.");
                    break;
            }
        };

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, ev) =>
        {
            ev.Cancel = true;
            Say();
            Say("Detengo todo (Ctrl+C)...");
            cts.Cancel();
        };

        var result = await engine.RunAsync(jobs, ctx, cts.Token);
        if (progressShown) Say();
        Say();

        var key = jobs[0].Key;
        var status = result.Statuses[key];
        result.Files.TryGetValue(key, out var fileName);
        switch (status)
        {
            case JobStatus.OK:
                Say("Listo. Archivo final:");
                Say("   " + Path.Combine(o.Folder, fileName!));
                Say("El video completo que se descargó para cortar ya se borró (solo después de revisar el fragmento).");
                break;
            case JobStatus.WARN:
                Say("Terminó, pero el archivo no tiene los códecs esperados. Archivo:");
                Say("   " + Path.Combine(o.Folder, fileName!));
                break;
            case JobStatus.REVIEW:
                Say("No se pudo terminar el fragmento. Se conservó el video descargado:");
                Say("   " + Path.Combine(o.Folder, fileName ?? ""));
                break;
            case JobStatus.Skipped:
                Say("No se descargó nada: ese video ya estaba en el historial de descargados.");
                break;
            default:
                Say(result.Cancelled ? "Detenido por el usuario." : "No se pudo descargar el video.");
                if (result.Blocked) Say("YouTube pidió verificación (\"no soy un robot\"). Sin cookies a veces pasa: probá otro video más tarde.");
                break;
        }
        Say();
        Say("Registro detallado (en inglés, para reportar problemas):");
        Say("   " + log.FilePath);
        return status is JobStatus.OK or JobStatus.WARN ? 0 : 1;
    }

    private static Options? Parse(string[] args)
    {
        var positional = new List<string>();
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "videoprep-demo");
        string format = "mp4";
        string? tools = null;
        var install = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--carpeta" when i + 1 < args.Length: folder = Path.GetFullPath(args[++i]); break;
                case "--formato" when i + 1 < args.Length: format = args[++i].ToLowerInvariant(); break;
                case "--herramientas" when i + 1 < args.Length: tools = args[++i]; break;
                case "--descargar-herramientas": install = true; break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal)) return null;
                    positional.Add(args[i]);
                    break;
            }
        }
        if (positional.Count is < 2 or > 3) return null;
        return new Options(positional[0], positional[1], positional.Count == 3 ? positional[2] : "", folder, format, tools, install);
    }

    private static void PrintUsage()
    {
        Say("Uso:");
        Say("  videoprep --demo \"<enlace>\" <inicio> [<fin>] [--carpeta <carpeta>] [--formato mp4] [--herramientas <carpeta>] [--descargar-herramientas]");
        Say();
        Say("Ejemplo:");
        Say("  videoprep --demo \"https://www.youtube.com/watch?v=XXXXXXXXXXX\" 00:00:05:00 00:00:07:29");
        Say();
        Say("Inicio y fin usan código de tiempo HH:MM:SS:FF (como en Premiere). Sin fin = hasta el final.");
        Say("Más detalles en docs/DEMO.md.");
    }

    /// <summary>Reports on the calling thread (Progress&lt;T&gt; would post to the thread pool and print out of order).</summary>
    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
