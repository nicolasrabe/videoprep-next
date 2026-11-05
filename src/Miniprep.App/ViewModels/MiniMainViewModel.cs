using CommunityToolkit.Mvvm.ComponentModel;
using Miniprep.App.Logic;
using Miniprep.App.Services;

namespace Miniprep.App.ViewModels;

/// <summary>The window of miniprep: two tabs (Prep and Cola), the number on the Cola tab and the short notices.</summary>
public sealed partial class MiniMainViewModel : ObservableObject
{
    private readonly Action<Action> _post;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private CancellationTokenSource? _toastLife;

    public MiniMainViewModel(MiniServices svc, IMiniUi ui, IDownloader? downloader = null, ToolsGate? tools = null,
                             Action<Action>? post = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _post = post ?? (a => Avalonia.Threading.Dispatcher.UIThread.Post(a));
        _delay = delay ?? Task.Delay;
        Services = svc;
        Settings = MiniSettings.Load(svc.Paths.SettingsFile);
        Queue = MiniQueue.Load(svc.Paths.QueueFile, out var problem);
        if (problem is not null) svc.Log.Write("WARN  " + problem);
        var gate = new ToolsGate(svc.Tools);
        tools ??= gate;
        downloader ??= new Downloader(svc, tools);
        var run = new RunGate();
        Prep = new PrepViewModel(svc, Settings, Queue, ui, downloader, tools, run, Notify, _post, _delay);
        Cola = new QueueViewModel(svc, Settings, Queue, ui, downloader, tools, run, Notify, _post, _delay);
        Queue.Changed += () => _post(RefreshBadge);
        RefreshBadge();
    }

    public MiniServices Services { get; }
    public MiniSettings Settings { get; }
    public MiniQueue Queue { get; }
    public PrepViewModel Prep { get; }
    public QueueViewModel Cola { get; }
    public Loc L => Loc.Instance;

    [ObservableProperty] private int _selectedTab;
    [ObservableProperty] private int _badge;
    [ObservableProperty] private bool _showBadge;
    [ObservableProperty] private string _toast = "";
    [ObservableProperty] private bool _showToast;

    public bool IsPrepTab => SelectedTab == 0;
    public bool IsColaTab => SelectedTab == 1;

    partial void OnSelectedTabChanged(int value)
    {
        OnPropertyChanged(nameof(IsPrepTab));
        OnPropertyChanged(nameof(IsColaTab));
    }

    private void RefreshBadge()
    {
        Badge = Queue.PendingCount;
        ShowBadge = Badge > 0;
    }

    

    private void Notify(string text)
    {
        _post(() => { Toast = text; ShowToast = true; });
        _toastLife?.Cancel();
        _toastLife = new CancellationTokenSource();
        var life = _toastLife;
        _ = Task.Run(async () =>
        {
            try { await _delay(TimeSpan.FromMilliseconds(1800), life.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            _post(() => ShowToast = false);
        });
    }
}
