namespace Videoprep.Core.Import;

/// <summary>State of a found link in the review window ("Estado").</summary>
public enum CandidateState { New, Repeated, InQueue, Channel, Playlist, NotVideo, Unknown }

/// <summary>One row of "Enlaces encontrados en ...". Only <see cref="CandidateState.New"/> rows start checked.</summary>
public sealed record ImportCandidate(string Url, string Context, LinkHow How, string Site, CandidateState State)
{
    public bool CheckedByDefault => State == CandidateState.New;
}

/// <summary>Turns the links found in a file into review rows: cleaned, classified, repeats and queued links marked.</summary>
public static class ImportPlanner
{
    public static IReadOnlyList<ImportCandidate> Plan(IEnumerable<FoundLink> found, IEnumerable<string> queuedUrls)
    {
        var queued = queuedUrls.Select(LinkNormalizer.CompareKey).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new List<ImportCandidate>();
        foreach (var f in found)
        {
            var url = LinkNormalizer.ForQueue(f.Url);
            if (url is null) continue;   // not http/https: never used
            var cls = LinkClassifier.Classify(url);
            var key = LinkNormalizer.CompareKey(url);
            var state = cls.Kind switch
            {
                LinkKind.Channel => CandidateState.Channel,
                LinkKind.Playlist => CandidateState.Playlist,
                LinkKind.NotVideo => CandidateState.NotVideo,
                LinkKind.Unknown => CandidateState.Unknown,
                _ => CandidateState.New,
            };
            if (!seen.Add(key)) state = CandidateState.Repeated;
            else if (queued.Contains(key)) state = CandidateState.InQueue;
            rows.Add(new ImportCandidate(url, f.Context, f.How, cls.Site, state));
        }
        return rows;
    }
}
