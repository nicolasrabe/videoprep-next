using Videoprep.Core.Text;

namespace Videoprep.Core.Models;

/// <summary>Turns queue items into jobs (one per output file). Port of the job-building part of Start-Batch.</summary>
public static class JobPlanner
{
    /// <summary>
    /// <paramref name="nextSeriesNumber"/>: first number to use per series prefix (1, or highest existing + 1 after the
    /// user agreed). It is advanced as numbers are used, so several items sharing a prefix keep counting.
    /// </summary>
    public static IReadOnlyList<Job> Build(IEnumerable<QueueItem> items, IDictionary<string, int> nextSeriesNumber)
    {
        var jobs = new List<Job>();
        foreach (var it in items)
        {
            var outs = it.Clips.Count > 0 ? it.Clips : [new ClipRange("", "")];
            var total = outs.Count;
            for (var i = 0; i < total; i++)
            {
                string? label = null;
                if (it.Naming == NamingMode.Series)
                {
                    var n = nextSeriesNumber.TryGetValue(it.Prefix, out var v) ? v : 1;
                    label = it.Prefix + n;
                    nextSeriesNumber[it.Prefix] = n + 1;
                }
                else if (it.Naming == NamingMode.Custom)
                {
                    label = SafeName.Make(it.Custom);
                    if (total > 1) label += " - " + (i + 1);
                }
                jobs.Add(new Job
                {
                    GroupId = it.Id, Index = i, Count = total, Url = it.Url, Site = it.Site, Format = it.Format,
                    Cap = it.Cap, Label = label, Start = outs[i].Start, End = outs[i].End, Download = i == 0, Fps = it.Fps,
                });
            }
        }
        return jobs;
    }

    /// <summary>Series prefixes used by the items, in order, without repeats (to check the folder before starting).</summary>
    public static IReadOnlyList<string> SeriesPrefixes(IEnumerable<QueueItem> items) =>
        items.Where(i => i.Naming == NamingMode.Series).Select(i => i.Prefix).Distinct().ToList();
}
