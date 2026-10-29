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
// With --print "DET|..." (preview) the ok scenarios print one DET line instead of downloading;
// with --print "INFO|..." (queue row card) one INFO json line.
// Environment:
//   FAKE_YTDLP_SOURCE  media file to copy as the "download" (else a few dummy bytes)
//   FAKE_YTDLP_CALLS   file where each call's arguments are appended (one call per line, args separated by \u001f)
using System.Diagnostics;

var argv = args.ToList();
// Like the real yt-dlp with PYTHONUTF8=1: UTF-8 on every Windows code page.
Console.OutputEncoding = new System.Text.UTF8Encoding(false);
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

// Listings (Scraping). FAKE_YTDLP_LISTING = folder with <last URL segment>.jsonl files (e.g. videos.jsonl, shorts.jsonl,
// list-<id>.jsonl for playlists). A missing file answers like a channel without that tab; ".../gone" is a broken link. Respects -I 1:N.
// FAKE_YTDLP_SCENARIO=bot answers with the bot check instead.
if (argv.Contains("--flat-playlist") && Environment.GetEnvironmentVariable("FAKE_YTDLP_LISTING") is { Length: > 0 } listingDir)
{
    var listUrl = argv.Last();
    if (Environment.GetEnvironmentVariable("FAKE_YTDLP_SCENARIO") == "bot")
    {
        Console.Error.WriteLine("ERROR: [youtube:tab] x: Sign in to confirm you’re not a bot.");
        return 1;
    }
    var u = new Uri(listUrl);
    var listId = System.Web.HttpUtility.ParseQueryString(u.Query)["list"];
    var tabName = listId is not null ? "list-" + listId : u.AbsolutePath.TrimEnd('/').Split('/').Last();
    if (tabName == "gone")
    {
        Console.Error.WriteLine("ERROR: [generic] Unable to download webpage: HTTP Error 404: Not Found");
        return 1;
    }
    var file = Path.Combine(listingDir, tabName + ".jsonl");
    if (!File.Exists(file))
    {
        Console.Error.WriteLine($"ERROR: [youtube:tab] @fake: This channel does not have a {tabName} tab");
        return 1;
    }
    var max = int.MaxValue;
    if (Opt("-I") is { } range && range.StartsWith("1:", StringComparison.Ordinal)) max = int.Parse(range[2..]);
    foreach (var l in File.ReadLines(file).Where(l => l.StartsWith('{')).Take(max)) Console.WriteLine(l);
    return 0;
}

// Exact data (Scraping): one EX line per URL. FAKE_YTDLP_LISTING/exact.txt may give "id|yyyymmdd|seconds|live|availability".
if (Opt("--print") is { } exactPrint && exactPrint.StartsWith("EX|", StringComparison.Ordinal))
{
    var known = new Dictionary<string, string>();
    if (Environment.GetEnvironmentVariable("FAKE_YTDLP_LISTING") is { Length: > 0 } dir2 && File.Exists(Path.Combine(dir2, "exact.txt")))
        foreach (var l in File.ReadLines(Path.Combine(dir2, "exact.txt"))) known[l.Split('|')[0]] = l;
    foreach (var link in argv.Where(a => a.StartsWith("http", StringComparison.Ordinal)))
    {
        var lu = new Uri(link);
        var vid = System.Web.HttpUtility.ParseQueryString(lu.Query)["v"] ?? lu.AbsolutePath.TrimEnd('/').Split('/').Last();
        Console.WriteLine("EX|" + (known.TryGetValue(vid, out var line) ? line : $"{vid}|20260101|60|not_live|public"));
    }
    return 0;
}

var url = argv.LastOrDefault() ?? "";
var parts = new Uri(url).AbsolutePath.Trim('/').Split('/');
// FAKE_YTDLP_SCENARIO forces a scenario for real-looking links (e.g. a YouTube watch URL).
var scenario = Environment.GetEnvironmentVariable("FAKE_YTDLP_SCENARIO") is { Length: > 0 } forced ? forced : parts[0];
var linkUri = new Uri(url);
// YouTube watch links: the id is v=..., and the archive line says "youtube <id>" like the real yt-dlp.
var id = System.Web.HttpUtility.ParseQueryString(linkUri.Query)["v"] ?? (parts.Length > 1 ? parts[1] : "abc");
var extractor = linkUri.Host.Contains("youtube", StringComparison.OrdinalIgnoreCase) ? "youtube" : "fake";
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

// Queue row card (F3): one INFO json line. FAKE_YTDLP_INFO_DELAY_MS makes it slow (to test one-at-a-time).
if (Opt("--print") is { } infoPrint && infoPrint.StartsWith("INFO|", StringComparison.Ordinal))
{
    if (Environment.GetEnvironmentVariable("FAKE_YTDLP_INFO_DELAY_MS") is { Length: > 0 } ms) Thread.Sleep(int.Parse(ms));
    var desc = "Una película corta animada, hecha con software libre.\\n\\nLicencia Creative Commons.";
    Console.WriteLine("INFO|{\"title\": \"Fake video " + id + "\", \"channel\": \"Fake channel\", \"upload_date\": \"20141110\", " +
                      "\"view_count\": 8100000, \"duration\": 2292, \"duration_string\": \"38:12\", \"description\": \"" + desc + "\", " +
                      "\"thumbnail\": \"https://fake.test/thumb/" + id + ".jpg\"}");
    return 0;
}

if (Opt("--print") is { } print && print.StartsWith("DET|", StringComparison.Ordinal))
{
    Console.WriteLine($"DET|4:05|1080|30|avc1.640028|mp4a.40.2|20260101|Fake uploader|https://fake.test/thumb/{id}.jpg|Fake video {id}");
    return 0;
}

// Archive: skip what is already recorded, like yt-dlp (prints nothing, exit 0).
var archive = Opt("--download-archive");
if (archive is not null && File.Exists(archive) && File.ReadAllLines(archive).Contains($"{extractor} {id}"))
{
    Console.WriteLine($"[download] {id}: has already been recorded in the archive");
    return 0;
}

var folder = Opt("-P") ?? ".";
var template = Opt("-o") ?? "%(title,id).100s.%(ext)s";
var source = Environment.GetEnvironmentVariable("FAKE_YTDLP_SOURCE");
var ext = source is null ? "mp4" : Path.GetExtension(source).TrimStart('.');
// "fancy": a title with a full-width colon, like --windows-filenames makes of "Project: What". The real yt-dlp
// leaves it out of the console --print but writes it to --print-to-file (docs/VERIFY.md).
var title = scenario == "fancy" ? "Fancy： video " + id : "Fake video " + id;
var name = template.Replace("%(title,id).100s", title).Replace("%(ext)s", ext);
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
if (archive is not null) File.AppendAllText(archive, $"{extractor} {id}\n");

var toFile = argv.IndexOf("--print-to-file");
if (toFile >= 0 && toFile + 2 < argv.Count) File.AppendAllText(argv[toFile + 2], target + "\n", new System.Text.UTF8Encoding(false));
Console.WriteLine(target.Replace("：", ""));   // --print after_move:filepath (loses the full-width colon, like the real one)
return scenario switch
{
    "ok101" => 101,
    "partial" => 1,
    _ => 0,
};
