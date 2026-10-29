using CommunityToolkit.Mvvm.ComponentModel;
using Videoprep.Core.Engine;
using Videoprep.Core.Storage;

namespace Videoprep.App.ViewModels;

/// <summary>The Options window (port of Show-OptionsDialog). Edits a copy; OK applies it.</summary>
public sealed partial class OptionsViewModel : ObservableObject
{
    [ObservableProperty] private bool _subs;
    [ObservableProperty] private string _subLangs = "en,es";
    [ObservableProperty] private bool _thumbs;
    [ObservableProperty] private bool _archive;
    [ObservableProperty] private bool _fallback = true;
    [ObservableProperty] private bool _keepFull;
    [ObservableProperty] private decimal _crf = 14;
    [ObservableProperty] private decimal _limit;
    [ObservableProperty] private decimal _sleep = 1;
    [ObservableProperty] private bool _constantRate;

    public static OptionsViewModel From(Settings s) => new()
    {
        Subs = s.Subs, SubLangs = s.SubLangs, Thumbs = s.Thumbs, Archive = s.Archive, Fallback = s.Fallback,
        KeepFull = s.KeepFull, Crf = s.Crf, Limit = s.Limit, Sleep = s.Sleep, ConstantRate = s.ConstantRate,
    };

    public void ApplyTo(Settings s)
    {
        s.Subs = Subs;
        s.SubLangs = SubLangs.Trim();
        s.Thumbs = Thumbs;
        s.Archive = Archive;
        s.Fallback = Fallback;
        s.KeepFull = KeepFull;
        s.Crf = (int)Math.Clamp(Crf, 10, 28);
        s.Limit = (int)Math.Clamp(Limit, 0, 500);
        s.Sleep = (int)Math.Clamp(Sleep, 0, 30);
        s.ConstantRate = ConstantRate;
    }

    public static DownloadOptions ToDownloadOptions(Settings s) => new()
    {
        Subtitles = s.Subs, SubtitleLanguages = s.SubLangs, Thumbnail = s.Thumbs, SkipDownloaded = s.Archive,
        ConvertFallback = s.Fallback, KeepFull = s.KeepFull, Crf = s.Crf, LimitMBps = s.Limit, SleepRequests = s.Sleep,
    };
}
