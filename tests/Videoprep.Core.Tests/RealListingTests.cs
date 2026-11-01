using Videoprep.Core.Engine;
using Videoprep.Core.Logging;
using Videoprep.Core.Scraping;

namespace Videoprep.Core.Tests;

/// <summary>
/// A REAL listing against the site (never a download): run by hand with VIDEOPREP_REAL_URL, VIDEOPREP_REAL_FROM and VIDEOPREP_REAL_TO
/// (yyyy-MM-dd). It checks that no result is outside the range and writes what happened to VIDEOPREP_REAL_REPORT (or the test output).
/// yt-dlp and deno come from the repo's .tools folder. Skipped when the variables are not set, and never part of the normal CI.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Real", "si")]
public class RealListingTests
{
    private sealed class CountingRunner(IProcessRunner inner) : IProcessRunner
    {
        public List<string> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessSpec spec, Action<string> onLine, CancellationToken ct)
        {
            lock (Calls) Calls.Add(string.Join(' ', spec.Args.Where(a => !a.StartsWith("--js-runtimes", StringComparison.Ordinal))));
            return inner.RunAsync(spec, onLine, ct);
        }
    }

    private sealed class NoQuestions : ISearchPrompts
    {
        public List<string> Asked { get; } = [];
        public Task<ManyKnownChoice> AskManyKnown(int total, int requests) { Asked.Add($"many {total}"); return Task.FromResult(ManyKnownChoice.SearchAll); }
        public Task<Reached50Choice> AskReached50(int found) { Asked.Add($"50 {found}"); return Task.FromResult(Reached50Choice.Continue); }
        public Task<bool> AskConfirmMore(int missing) { Asked.Add($"more {missing}"); return Task.FromResult(false); }
    }

    private static string? ToolsDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, ".tools"))) return Path.Combine(d.FullName, ".tools");
        return null;
    }

    [Fact]
    public async Task Real_listing_of_a_channel_in_a_date_range_has_nothing_outside_the_range()
    {
        var url = Environment.GetEnvironmentVariable("VIDEOPREP_REAL_URL");
        var from = Environment.GetEnvironmentVariable("VIDEOPREP_REAL_FROM");
        var to = Environment.GetEnvironmentVariable("VIDEOPREP_REAL_TO");
        Assert.SkipWhen(string.IsNullOrEmpty(url) || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to), "VIDEOPREP_REAL_URL/FROM/TO not set");
        var tools = ToolsDir();
        Assert.SkipWhen(tools is null || !File.Exists(Path.Combine(tools, "yt-dlp.exe")), "no .tools/yt-dlp.exe");
        var deno = Path.Combine(tools!, "deno.exe");

        var f = new FilterSet { From = DateOnly.Parse(from!, System.Globalization.CultureInfo.InvariantCulture), To = DateOnly.Parse(to!, System.Globalization.CultureInfo.InvariantCulture), SkipDownloaded = false };
        var runner = new CountingRunner(new ProcessRunner());
        var search = new ScrapeSearch(runner, new Log());
        var prompts = new NoQuestions();
        var req = new SearchRequest(url!, [SourceTab.Videos], f, _ => false, DateOnly.FromDateTime(DateTime.Today),
            new ToolPaths(Path.Combine(tools!, "yt-dlp.exe"), null, null, File.Exists(deno) ? deno : null), CookieSource.NoCookies);

        var r = await search.RunAsync(req, prompts, null, TestContext.Current.CancellationToken);

        var outside = r.Eval.Shown.Where(e => e.Date is not { } d || e.DateIsApprox || d < f.From || d > f.To).ToList();
        var report = new List<string>
        {
            $"channel: {url}", $"range: {from} .. {to} (both included)", $"today: {DateOnly.FromDateTime(DateTime.Today):yyyy-MM-dd}",
            $"status: {r.Status}", $"requests (yt-dlp runs, one exact-date request per video): {r.Requests}", $"yt-dlp processes started: {runner.Calls.Count}",
            $"entries read: {r.Eval.Reviewed}", $"videos shown: {r.Eval.Shown.Count}", $"without a confirmed date: {r.Eval.Unconfirmed.Count}",
            $"outside the range: {outside.Count}", $"questions asked: {(prompts.Asked.Count == 0 ? "none" : string.Join(", ", prompts.Asked))}", "", "SHOWN:",
        };
        report.AddRange(r.Eval.Shown.Select(e => $"  {e.Date:yyyy-MM-dd}{(e.DateIsApprox ? " ~" : "")}  {e.Id}  {e.Title}"));
        report.Add("WITHOUT A CONFIRMED DATE:");
        report.AddRange(r.Eval.Unconfirmed.Select(e => $"  {(e.Date is { } d ? d.ToString("yyyy-MM-dd") : "????-??-??")}{(e.DateIsApprox ? " ~" : "")}  {e.Id}  {e.Title}"));
        report.Add("PROCESS CALLS:");
        report.AddRange(runner.Calls.Select(c => "  " + (c.Length > 220 ? c[..220] + "..." : c)));
        var path = Environment.GetEnvironmentVariable("VIDEOPREP_REAL_REPORT");
        if (!string.IsNullOrEmpty(path)) File.WriteAllLines(path, report);

        Assert.Empty(outside);
        Assert.Equal(SearchStatus.Done, r.Status);
    }
}
