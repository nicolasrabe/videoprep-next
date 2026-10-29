using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Videoprep.App.Services;
using Videoprep.Core.Models;
using Videoprep.Core.Text;

namespace Videoprep.App.ViewModels;

/// <summary>One clip row in the Download tab: start, end, and the X button. Invalid text turns red (legacy Test-ClipText).</summary>
public sealed partial class ClipRowViewModel(Action<ClipRowViewModel> remove) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartValid), nameof(StartBeyond), nameof(StartBad), nameof(Warning), nameof(HasWarning))]
    private string _start = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndValid), nameof(EndBeyond), nameof(EndBad), nameof(Warning), nameof(HasWarning))]
    private string _end = "";

    private double? _duration;
    private string _durationText = "";
    private double? _fps;

    public bool StartValid => IsOk(Start);
    public bool EndValid => IsOk(End);

    /// <summary>F2 fragment warning: beyond the known video duration (only a warning, never blocks).</summary>
    public bool StartBeyond => _duration is { } d && QueueEdits.BeyondDuration(Start, d, _fps, isEnd: false);
    public bool EndBeyond => _duration is { } d && QueueEdits.BeyondDuration(End, d, _fps, isEnd: true);
    public bool StartBad => !StartValid || StartBeyond;
    public bool EndBad => !EndValid || EndBeyond;
    /// <summary>"El video dura 38:12" when a field is beyond the end; else "".</summary>
    public string Warning => StartBeyond || EndBeyond ? Loc.Instance.Format("clip_too_long", _durationText) : "";
    public bool HasWarning => Warning.Length > 0;

    /// <summary>The preview found (or lost) the video duration.</summary>
    public void SetDuration(double? seconds, string text, double? fps)
    {
        _duration = seconds;
        _durationText = text;
        _fps = fps;
        foreach (var p in new[] { nameof(StartBeyond), nameof(EndBeyond), nameof(StartBad), nameof(EndBad), nameof(Warning), nameof(HasWarning) })
            OnPropertyChanged(p);
    }
    public bool IsEmpty => Start.Trim().Length == 0 && End.Trim().Length == 0;

    private static bool IsOk(string text)
    {
        var tc = Timecode.Parse(text);
        return tc.Blank || tc.Valid;
    }

    [RelayCommand]
    private void Remove() => remove(this);
}

/// <summary>A format in the combo box: key + label.</summary>
public sealed record FormatChoice(string Key, string Label)
{
    public override string ToString() => Label;
}
