// Fake yt-dlp for engine tests. It never touches the network.
//
// The scenario is the last path segment of the URL: https://fake.test/<scenario>/<id>
//   ok            progress lines, "downloads" the file, prints its final path, exit 0
//   ok101         like ok but exits 101 (yt-dlp "stopped early on purpose")
//   error         prints a yt-dlp error, exit 1
//   bot           prints the YouTube bot check, exit 1
//   bot-unless-X  bot check unless --cookies-from-browser X (then like ok)
//   partial       writes the file and prints its path, but exits 1
//   slow          prints progress for 60 s (to test Stop); starts a child process too (to test killing the tree)
//   noformat      "Requested format is not available", exit 1
//   hang          prints nothing for 60 s (to test the preview time limit)
// With --print "DET|..." (preview) the ok scenarios print one DET line instead of downloading.
// Environment:
//   FAKE_YTDLP_SOURCE  media file to copy as the "download" (else a few dummy bytes)
//   FAKE_YTDLP_CALLS   file where each call's arguments are appended (one call per line, args separated by \u001f)
using System.Diagnostics;

var argv = args.ToList();
string? Opt(string name)
{
    var i = argv.IndexOf(name);
    return i >= 0 && i + 1 < argv.Count ? argv[i + 1] : null;
}

if (Environment.GetEnvironmentVariable("FAKE_YTDLP_CALLS") is { Length: > 0 } calls)
    File.AppendAllText(calls, string.Join('\u001f', args) + "\n");

if (argv.Contains("--fake-child-sleep"))
{
    Thread.Sleep(60_000);
    return 0;
}
if (argv.Contains("-U"))
{
    Console.WriteLine("Latest version: fake (yt-dlp is up to date)");
    return 0;
}

var url = argv.LastOrDefault() ?? "";
var parts = new Uri(url).AbsolutePath.Trim('/').Split('/');
// FAKE_YTDLP_SCENARIO forces a scenario for real-looking links (e.g. a YouTube watch URL).
var scenario = Environment.GetEnvironmentVariable("FAKE_YTDLP_SCENARIO") is { Length: > 0 } forced ? forced : parts[0];
var id = parts.Length > 1 ? parts[1] : "abc";
var browser = Opt("--cookies-from-browser");
var cookieFile = Opt("--cookies");

Console.WriteLine($"[fake] Extracting URL: {url}");

if (scenario.StartsWith("bot-unless-", StringComparison.Ordinal))
    scenario = browser == scenario["bot-unless-".Length..] ? "ok" : "bot";

switch (scenario)
{
    case "noformat":
        Console.Error.WriteLine($"ERROR: [youtube] {id}: Requested format is not available. Use --list-formats for a list of available formats");
        return 1;
    case "hang":
        Thread.Sleep(60_000);
        return 0;
    case "error":
        Console.Error.WriteLine($"ERROR: [fake] {id}: Unable to download webpage: HTTP Error 404: Not Found");
        return 1;
    case "bot":
        Console.Error.WriteLine($"ERROR: [youtube] {id}: Sign in to confirm you’re not a bot. Use --cookies-from-browser or --cookies for the authentication.");
        return 1;
    case "slow":
        using (var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--fake-child-sleep") { UseShellExecute = false }))
        {
            File.WriteAllText(Path.Combine(Opt("-P") ?? ".", "fake-child.pid"), child!.Id.ToString());
            for (var i = 0; i < 300; i++)
            {
                Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"VP|{i * 0.3,5:0.0}%|  1.00MiB/s|00:59"));
                Thread.Sleep(200);
            }
        }
        return 0;
}

if (Opt("--print") is { } print && print.StartsWith("DET|", StringComparison.Ordinal))
{
    Console.WriteLine($"DET|4:05|1080|30|avc1.640028|mp4a.40.2|20260101|Fake uploader|https://fake.test/thumb/{id}.jpg|Fake video {id}");
    return 0;
}

// Archive: skip what is already recorded, like yt-dlp (prints nothing, exit 0).
var archive = Opt("--download-archive");
if (archive is not null && File.Exists(archive) && File.ReadAllLines(archive).Contains($"fake {id}"))
{
    Console.WriteLine($"[download] {id}: has already been recorded in the archive");
    return 0;
}

var folder = Opt("-P") ?? ".";
var template = Opt("-o") ?? "%(title,id).100s.%(ext)s";
var source = Environment.GetEnvironmentVariable("FAKE_YTDLP_SOURCE");
var ext = source is null ? "mp4" : Path.GetExtension(source).TrimStart('.');
var name = template.Replace("%(title,id).100s", "Fake video " + id).Replace("%(ext)s", ext);
var target = Path.GetFullPath(Path.Combine(folder, name));
Directory.CreateDirectory(Path.GetDirectoryName(target)!);

if (File.Exists(target))
{
    Console.WriteLine($"[download] {target} has already been downloaded");
}
else
{
    foreach (var pct in new[] { "  0.0%", " 41.5%", "100.0%" }) Console.WriteLine($"VP|{pct}|   7.16MiB/s|00:00");
    if (source is not null) File.Copy(source, target);
    else File.WriteAllBytes(target, [1, 2, 3]);
}

// --cookies FILE together with a browser: yt-dlp saves the jar there.
if (cookieFile is not null && browser is not null)
    File.WriteAllText(cookieFile, "# Netscape HTTP Cookie File\n.fake.test\tTRUE\t/\tTRUE\t0\tSID\tfake\n");
if (archive is not null) File.AppendAllText(archive, $"fake {id}\n");

Console.WriteLine(target);   // --print after_move:filepath
return scenario switch
{
    "ok101" => 101,
    "partial" => 1,
    _ => 0,
};
