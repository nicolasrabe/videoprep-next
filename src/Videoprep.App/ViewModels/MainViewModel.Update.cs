using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Videoprep.Core.Update;

namespace Videoprep.App.ViewModels;

/// <summary>
/// F9: new versions of videoprep. A check when it opens (once a day at most, can be turned off) and a button in Ajustes;
/// installing always needs the person's click, and never happens while something downloads.
/// </summary>
public sealed partial class MainViewModel
{
    [ObservableProperty] private bool _updateCheckOn;
    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateBannerText), nameof(UpdateBannerVisible))]
    private ReleaseInfo? _updateAvailable;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(UpdateBannerVisible))] private bool _updateBannerDismissed;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsIdle))] private bool _updating;

    public string UpdateBannerText => UpdateAvailable is { } r ? L.Format("upd_banner", r.Version) : "";
    public bool UpdateBannerVisible => UpdateAvailable is not null && !UpdateBannerDismissed;
    public string InstalledVersionText => L.Format("upd_installed", _svc.CurrentVersion);

    private UpdateStager? _stager;
    private CancellationTokenSource? _updateCts;
    private bool _checkingUpdates;

    private void InitUpdates()
    {
        UpdateCheckOn = Settings.UpdateCheck;
        if (_svc.Updates is { } client)
            _stager = new UpdateStager(client, _svc.Paths.UpdatesDir, _svc.VersionOf);
    }

    partial void OnUpdateCheckOnChanged(bool value)
    {
        if (_loadingSettings) return;
        Settings.UpdateCheck = value;
        SaveSettings();
    }

    /// <summary>Called once the window is open: tells what the last update did, tidies, and looks for a new version if it is time.</summary>
    private async Task OnShownUpdatesAsync()
    {
        if (UpdateResult.Take(_svc.Paths.UpdatesDir) is { } last) await ReportLastUpdateAsync(last).ConfigureAwait(true);
        _stager?.CleanOld();
        if (_svc.Updates is null || !Settings.UpdateCheck || !UpdateChecker.IsDue(Settings.UpdateLast, DateTimeOffset.Now)) return;
        await CheckForUpdateAsync(manual: false).ConfigureAwait(true);
    }

    private async Task ReportLastUpdateAsync(UpdateResult last)
    {
        _svc.Log.Write(last.Ok
            ? $"Update: videoprep {last.From} -> {last.To} done."
            : $"WARN  Update to {last.To} failed ({last.Failure}): {last.Detail}");
        if (last.Ok)
            await _ui.ShowMessage(L.Format("upd_done", last.To)).ConfigureAwait(true);
        else
            await _ui.ShowMessage(L.Format("upd_failed_before", last.To, FailureText(last.Failure ?? UpdateFailure.Network)), warning: true).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CheckUpdatesNowAsync() => await CheckForUpdateAsync(manual: true).ConfigureAwait(true);

    private async Task CheckForUpdateAsync(bool manual)
    {
        if (_svc.Updates is not { } client || _checkingUpdates || Updating) return;
        _checkingUpdates = true;
        if (manual) UpdateStatus = L["upd_checking"];
        try
        {
            var found = await new UpdateChecker(client).CheckAsync(_svc.CurrentVersion, _svc.Rid, CancellationToken.None).ConfigureAwait(true);
            Settings.UpdateLast = DateTimeOffset.Now.ToString("o");
            SaveSettings();
            UpdateAvailable = found;
            UpdateBannerDismissed = false;
            if (found is null)
            {
                _svc.Log.Write($"Update check: videoprep {_svc.CurrentVersion} is the newest.");
                if (manual) UpdateStatus = L.Format("upd_uptodate", _svc.CurrentVersion);
            }
            else
            {
                _svc.Log.Write($"Update check: videoprep {found.Version} is available.");
                UpdateStatus = L.Format("upd_banner", found.Version);
            }
        }
        catch (UpdateException ex)
        {
            _svc.Log.Write($"WARN  Update check failed ({ex.Failure}): {ex.Message}");
            if (manual) UpdateStatus = L["upd_check_failed"];
        }
        finally { _checkingUpdates = false; }
    }

    [RelayCommand]
    private void DismissUpdate() => UpdateBannerDismissed = true;

    [RelayCommand]
    private void OpenReleaseNotes()
    {
        if (UpdateAvailable is { } r && UpdateEndpoint.IsAllowed(r.PageUrl)) _ui.OpenUrl(r.PageUrl.ToString());
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (UpdateAvailable is not { } release) return;
        if (!IsIdle)
        {
            await _ui.ShowMessage(L["upd_busy"]).ConfigureAwait(true);
            return;
        }
        if (!_svc.CanSelfUpdate || release.Zip is null || release.Sums is null || _stager is null)
        {
            await OfferPageAsync(release, L["upd_manual"]).ConfigureAwait(true);
            return;
        }
        if (UpdatePermissions.Check(_svc.CurrentExe) is { } cannot)
        {
            await OfferPageAsync(release, cannot == UpdateFailure.NoWritePermission ? L["upd_nowrite"] : L["upd_manual"]).ConfigureAwait(true);
            return;
        }
        if (!await _ui.AskChoice("videoprep", L.Format("upd_confirm", release.Version), L["upd_install"], L["btn_cancel"]).ConfigureAwait(true)) return;

        Updating = true;
        StagedUpdate staged;
        try
        {
            var progress = new Progress<double>(p => _post(() => StatusText = L.Format("upd_downloading", release.Version, (int)(p * 100))));
            StatusText = L.Format("upd_downloading", release.Version, 0);
            _updateCts = new CancellationTokenSource();
            staged = await _stager.PrepareAsync(release, progress, _updateCts.Token).ConfigureAwait(true);
        }
        catch (UpdateException ex)
        {
            _svc.Log.Write($"WARN  Update to {release.Version} failed ({ex.Failure}): {ex.Message}");
            Updating = false;
            StatusText = L["st_ready"];
            await OfferPageAsync(release, FailureText(ex.Failure)).ConfigureAwait(true);
            return;
        }
        catch (OperationCanceledException)
        {
            Updating = false;
            StatusText = L["st_ready"];
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _svc.Log.Write($"WARN  Update to {release.Version} failed (files): {ex.Message}");
            Updating = false;
            StatusText = L["st_ready"];
            await OfferPageAsync(release, L["upd_fail_files"]).ConfigureAwait(true);
            return;
        }

        // The new program does the swap after this one closes. Settings and queue are saved first.
        SaveSettings();
        SaveQueue();
        var request = new ApplyRequest(_svc.CurrentExe!, staged.ExePath, staged.Sha256, Environment.ProcessId, _svc.CurrentVersion.ToString(), staged.Version.ToString());
        _svc.Log.Write($"Update: starting the installer of {staged.Version} and closing videoprep.");
        if (!await _svc.StartProcess(staged.ExePath, request.ToArguments()).ConfigureAwait(true))
        {
            Updating = false;
            StatusText = L["st_ready"];
            await OfferPageAsync(release, L["upd_fail_start"]).ConfigureAwait(true);
            return;
        }
        _ui.CloseApp();
    }

    private async Task OfferPageAsync(ReleaseInfo release, string why)
    {
        if (await _ui.AskChoice("videoprep", why + "\n\n" + L["upd_openpage_q"], L["upd_openpage"], L["btn_cancel"]).ConfigureAwait(true)
            && UpdateEndpoint.IsAllowed(release.PageUrl))
            _ui.OpenUrl(release.PageUrl.ToString());
    }

    private string FailureText(UpdateFailure f) => f switch
    {
        UpdateFailure.Network => L["upd_fail_network"],
        UpdateFailure.UnsafeAddress => L["upd_fail_address"],
        UpdateFailure.TooBig => L["upd_fail_big"],
        UpdateFailure.NoInstaller => L["upd_manual"],
        UpdateFailure.NoChecksum => L["upd_fail_nosum"],
        UpdateFailure.ChecksumMismatch => L["upd_fail_sum"],
        UpdateFailure.BadArchive or UpdateFailure.BadExecutable or UpdateFailure.WrongVersion => L["upd_fail_file"],
        UpdateFailure.NoWritePermission => L["upd_nowrite"],
        UpdateFailure.FilesLocked => L["upd_fail_locked"],
        UpdateFailure.NewVersionDidNotStart => L["upd_fail_nostart"],
        _ => L["upd_fail_network"],
    };
}
