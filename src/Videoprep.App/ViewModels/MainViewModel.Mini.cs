using System.Collections.Specialized;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Videoprep.App.Services;
using Videoprep.Core.Engine;
using Videoprep.Core.Models;

namespace Videoprep.App.ViewModels;

/// <summary>What the mini window says (F4); the color of the text and the bar follow it.</summary>
public enum MiniKind { Idle, Downloading, Busy, Problems, Notice }

/// <summary>
/// The small windows (F4, RONDA 2): the card (mini) and the one-line bar (micro). They show the file being saved and its percentage,
/// "N/M" with several videos, "Copiá un enlace para descargar" when idle, red text when something failed, and brief notices
/// (links added from the clipboard). They never show a link or a cookie: while a title is unknown they say "Descargando...".
/// </summary>
public sealed partial class MainViewModel
{
    [ObservableProperty] private bool _isMiniActive;

    /// <summary>Maxi = the full window, Mini = the card, Micro = the one-line bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMiniCard), nameof(IsMicroBar))]
    private MiniLevel _miniLevel = MiniLevel.Maxi;

    public bool IsMiniCard => MiniLevel == MiniLevel.Mini;
    public bool IsMicroBar => MiniLevel == MiniLevel.Micro;

    /// <summary>Speed of the current download ("2,1MiB/s"), for the card's status line.</summary>
    [ObservableProperty] private string _miniSpeed = "";

    /// <summary>The card's "Fragmento" box: on = one fragment row (start and end) is open in the draft; off = no fragments.</summary>
    public bool MiniHasFragment
    {
        get => Clips.Count > 0;
        set
        {
            if (value == Clips.Count > 0) return;
            if (value) AddClipRow("", "");
            else Clips.Clear();
        }
    }

    /// <summary>The first fragment of the draft (what the card's start and end boxes edit).</summary>
    public ClipRowViewModel? MiniClip => Clips.FirstOrDefault();
    [ObservableProperty] private string _miniText = "";
    [ObservableProperty] private string _miniPercentText = "";
    [ObservableProperty] private string _miniCounter = "";
    [ObservableProperty] private double _miniPercent;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MiniIsProblem))]
    private MiniKind _miniKind;

    public bool MiniIsProblem => MiniKind == MiniKind.Problems;

    /// <summary>"La ventana mini queda siempre encima" (Settings). Changing it takes effect at once, also while the mini is open.</summary>
    [ObservableProperty] private bool _miniOnTop;

    partial void OnMiniOnTopChanged(bool value)
    {
        if (_loadingSettings) return;
        Settings.MiniOnTop = value;
        SaveSettings();
    }

    // What the line is made from.
    private string? _miniName;
    private int _miniNumber, _miniTotal;
    private bool _miniJobActive;
    private (int Good, int Bad)? _miniResult;
    private bool _miniStopped;
    private string? _miniNotice;
    private DispatcherTimer? _miniNoticeTimer;

    [RelayCommand]
    private void ToggleMini()
    {
        if (IsMiniActive) CloseMini();
        else OpenMini();
    }

    private void OpenMini()
    {
        // The big window may be hidden by a question that is waiting: only the idle window shrinks.
        IsMiniActive = true;
        MiniLevel = MiniLevel.Mini;
        RefreshMini();
        _ui.SetMiniLevel(MiniLevel.Mini);
    }

    /// <summary>"⤢" and the double click: one level up (micro to card, card to the full window).</summary>
    [RelayCommand]
    public void PromoteMini()
    {
        if (!IsMiniActive) return;
        if (MiniLevel == MiniLevel.Micro)
        {
            MiniLevel = MiniLevel.Mini;
            _ui.SetMiniLevel(MiniLevel.Mini);
        }
        else
        {
            CloseMini();
        }
    }

    /// <summary>"▁": the card shrinks to the bar.</summary>
    [RelayCommand]
    public void DemoteMini()
    {
        if (!IsMiniActive || MiniLevel != MiniLevel.Mini) return;
        MiniLevel = MiniLevel.Micro;
        _ui.SetMiniLevel(MiniLevel.Micro);
    }

    /// <summary>"Cerrar modo mini": the big window comes back. Also called when the user restores it from the taskbar.</summary>
    [RelayCommand]
    public void CloseMini()
    {
        if (!IsMiniActive) return;
        IsMiniActive = false;
        MiniLevel = MiniLevel.Maxi;
        _ui.CloseMini();
        _ = ResolvePendingClipboardAsync();
    }

    /// <summary>"Cerrar videoprep" in the mini window's menu: the big window closes (it asks first if something is running).</summary>
    [RelayCommand]
    private void QuitFromMini() => _ui.CloseApp();

    private void ResetMiniRun()
    {
        MiniSpeed = "";
        _miniName = null;
        _miniNumber = _miniTotal = 0;
        _miniJobActive = false;
        _miniResult = null;
        _miniStopped = false;
    }

    /// <summary>A short message in the mini window and the status line ("Se agregó 1 enlace a la cola").</summary>
    private void ShowNotice(string text)
    {
        _miniNotice = text;
        if (!IsRunning) StatusText = text;
        RefreshMini();
        _miniNoticeTimer?.Stop();
        _miniNoticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _miniNoticeTimer.Tick += (_, _) =>
        {
            _miniNoticeTimer?.Stop();
            _miniNotice = null;
            RefreshMini();
        };
        _miniNoticeTimer.Start();
    }

    /// <summary>The name the mini shows for a job: its own name or the video title; never the link.</summary>
    private string? MiniNameFor(Job job)
    {
        if (job.Label is { Length: > 0 } label) return label;
        var item = Items.FirstOrDefault(i => i.Id == job.GroupId);
        return item is null ? null : TitleOf(item);
    }

    private void RefreshMini()
    {
        MiniCounter = "";
        MiniPercentText = "";
        MiniPercent = 0;
        if (_miniNotice is not null)
        {
            (MiniKind, MiniText) = (MiniKind.Notice, _miniNotice);
        }
        else if (_pendingClipboard.Count > 0)
        {
            (MiniKind, MiniText) = (MiniKind.Notice, L.Format("mini_pending", _pendingClipboard.Count));
        }
        else if (IsRunning && _miniJobActive)
        {
            MiniKind = MiniKind.Downloading;
            MiniText = _miniName is { Length: > 0 } n ? n : L["mini_downloading"];
            MiniPercent = Math.Clamp(Progress, 0, 100);
            if (!ProgressIndeterminate && Progress > 0) MiniPercentText = $"{Math.Round(Progress):0} %";
            if (_miniTotal > 1) MiniCounter = $"{_miniNumber}/{_miniTotal}";
        }
        else if (IsRunning)
        {
            // First-time setup or an update of the tools: the same text as the status line (no links in it).
            MiniKind = MiniKind.Busy;
            MiniText = StatusText;
            MiniPercent = ProgressIndeterminate ? 0 : Math.Clamp(Progress, 0, 100);
        }
        else if (_miniStopped)
        {
            (MiniKind, MiniText) = (MiniKind.Idle, L["mini_stopped"]);
        }
        else if (_miniResult is { } r)
        {
            if (r.Bad > 0) (MiniKind, MiniText) = (MiniKind.Problems, L.Format("mini_problems", r.Good, r.Bad));
            else (MiniKind, MiniText) = (MiniKind.Idle, L.Format("mini_done", r.Good, r.Good + r.Bad));
        }
        else
        {
            (MiniKind, MiniText) = (MiniKind.Idle, L["mini_idle"]);
        }
    }

    partial void OnProgressChanged(double value) => RefreshMini();

    partial void OnStatusTextChanged(string value)
    {
        if (IsRunning && !_miniJobActive) RefreshMini();
    }

    /// <summary>A job of the engine started or moved on: the mini shows its name and "N/M".</summary>
    private void MiniJobStarted(Job job, int number, int total)
    {
        _miniName = MiniNameFor(job);
        MiniSpeed = "";
        (_miniNumber, _miniTotal, _miniJobActive) = (number, total, true);
        RefreshMini();
    }

    private void MiniRunFinished(BatchResult result)
    {
        _miniJobActive = false;
        MiniSpeed = "";
        _miniStopped = result.Cancelled;
        _miniResult = result.Cancelled ? null : (result.Ok + result.Warn, result.Fail);
        RefreshMini();
    }
}
