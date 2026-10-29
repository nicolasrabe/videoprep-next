using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Videoprep.Core.Formats;
using Videoprep.Core.Profiles;

namespace Videoprep.App.ViewModels;

/// <summary>A row of the profiles list in Ajustes.</summary>
public sealed record ProfileRow(FormatProfile Profile, string Label, string Detail, bool BuiltIn);

/// <summary>What is about to happen to a profile that subscriptions follow (the warning says it in different words).</summary>
public enum ProfileChange { Edit, Rename, Delete }

/// <summary>
/// Round 2, part 2: format profiles (F6) in the Download tab, saved in profiles.json. Using them in the queue dialog, the
/// import window and Scraping lives in those view models; this part keeps the list, saves, renames, deletes, exports and
/// imports, and warns before a change reaches subscriptions that follow the profile.
/// </summary>
public sealed partial class MainViewModel
{
    public ProfileStore Profiles { get; private set; } = null!;

    /// <summary>The Download tab's box: picking one fills the format, the resolution and the names.</summary>
    public ObservableCollection<ProfileChoice> ProfileChoices { get; } = [];
    [ObservableProperty] private ProfileChoice? _selectedProfileChoice;

    public ObservableCollection<ProfileRow> ManagedProfiles { get; } = [];
    [ObservableProperty] private ProfileRow? _selectedManagedProfile;

    private void InitProfiles()
    {
        Profiles = ProfileStore.Load(_svc.Paths.ProfilesFile, out var problem);
        if (problem is not null) _svc.Log.Write("WARN  " + problem);
        RebuildProfileChoices();
    }

    /// <summary>Names of the three examples in the current language ("Solo audio (M4A)" / "Audio only (M4A)").</summary>
    public string ProfileLabel(FormatProfile p) => p.Id switch
    {
        "builtin:premiere-1080" => L["prof_b_premiere"],
        "builtin:prores-4k" => L["prof_b_prores"],
        "builtin:audio-m4a" => L["prof_b_audio"],
        _ => p.Name,
    };

    /// <summary>Example names in both languages: a profile of the user cannot take one of them.</summary>
    private IEnumerable<string> ReservedProfileNames() =>
        ProfileStore.BuiltIns.Select(ProfileLabel).Concat(["Solo audio (M4A)", "Audio only (M4A)"]);

    internal IReadOnlyList<ProfileChoice> ProfileList() => Profiles.All.Select(p => new ProfileChoice(p, ProfileLabel(p))).ToList();

    /// <summary>The label of the profile with this id, or null when there is none (deleted).</summary>
    internal string? ProfileNameOf(string? id) => Profiles.Find(id) is { } p ? ProfileLabel(p) : null;

    private static string ProfileDetail(FormatProfile p)
    {
        var label = FormatLabelOf(p.Format, p.Cap);
        return p.IsSeries ? $"{label} · {p.Prefix}1, {p.Prefix}2..." : label;
    }

    private static string FormatLabelOf(string format, int cap)
    {
        var f = FormatCatalog.Get(format);
        return f.Kind == MediaKind.Audio ? f.Label : $"{f.Label} · {FormatCatalog.ResolutionLabel(cap)}";
    }

    private void RebuildProfileChoices()
    {
        var list = ProfileList();
        ProfileChoices.Clear();
        foreach (var c in list) ProfileChoices.Add(c);
        var keep = SelectedManagedProfile?.Profile.Id;
        ManagedProfiles.Clear();
        foreach (var p in Profiles.All) ManagedProfiles.Add(new ProfileRow(p, p.BuiltIn ? ProfileLabel(p) + " (" + L["prof_example"] + ")" : ProfileLabel(p), ProfileDetail(p), p.BuiltIn));
        SelectedManagedProfile = ManagedProfiles.FirstOrDefault(r => r.Profile.Id == keep);
        Scraping?.Explore.Format.SetProfiles(list);
        Scraping?.Subscriptions.RefreshProfileNames();
    }

    partial void OnSelectedProfileChoiceChanged(ProfileChoice? value)
    {
        if (value?.Profile is not { } p) return;
        ApplyProfile(p);
        _post(() => SelectedProfileChoice = null);   // the box goes back to "Elegir un perfil..."
    }

    /// <summary>Fills format, resolution and names of the Download tab from a profile.</summary>
    public void ApplyProfile(FormatProfile p)
    {
        IsAudio = p.Kind == MediaKind.Audio;
        SelectedFormat = FormatChoices.FirstOrDefault(f => f.Key == p.Format) ?? SelectedFormat;
        var label = FormatCatalog.ResolutionLabel(p.Cap);
        if (ResolutionChoices.Contains(label)) SelectedResolution = label;
        if (p.IsSeries)
        {
            NameSeries = true;
            if (p.Prefix.Length > 0) SeriesText = p.Prefix;
        }
        else
        {
            NameOriginal = true;
        }
    }

    private FormatProfile CurrentChoices() => new()
    {
        Format = SelectedFormatKey, Cap = SelectedCap, Naming = NameSeries ? "series" : "orig",
        Prefix = NameSeries ? SeriesText.Trim() : "",
    };

    // ---------------- subscriptions affected by a change ----------------

    /// <summary>"Cancelar / Continuar" before a change reaches subscriptions that follow the profile. True = go on.</summary>
    private async Task<bool> ConfirmProfileChangeAsync(FormatProfile p, ProfileChange change)
    {
        var names = Scraping.Subscriptions.NamesUsing(p.Id);
        if (names.Count == 0) return true;
        var nl = Environment.NewLine;
        var list = nl + nl + string.Join(nl, names.Select(n => "  • " + n)) + nl + nl;
        var key = change switch { ProfileChange.Edit => "msg_prof_subs_edit", ProfileChange.Rename => "msg_prof_subs_rename", _ => "msg_prof_subs_delete" };
        return await _ui.AskChoice(L["grp_profiles"], L.Format(key, ProfileLabel(p), list), L["btn_continue"], L["btn_cancel"]);
    }

    // ---------------- commands ----------------

    /// <summary>"Guardar como perfil...": a name for what the Download tab has now. An existing name is replaced after asking.</summary>
    [RelayCommand]
    private async Task SaveProfile()
    {
        var name = await _ui.AskText(L["dlg_prof_save"], L["lbl_prof_name"], "");
        if (name is null) return;
        var clean = FormatProfile.CleanName(name);
        if (clean.Length == 0)
        {
            await _ui.ShowMessage(L["msg_prof_empty"], warning: true);
            return;
        }
        if (ProfileStore.BuiltInNameTaken(clean, ReservedProfileNames()))
        {
            await _ui.ShowMessage(L["msg_prof_taken"], warning: true);
            return;
        }
        var existing = Profiles.User.FirstOrDefault(p => string.Equals(p.Name, clean, StringComparison.CurrentCultureIgnoreCase));
        if (existing is not null)
        {
            if (!await _ui.AskYesNo(L["dlg_prof_save"], L.Format("msg_prof_overwrite", existing.Name))) return;
            if (!await ConfirmProfileChangeAsync(existing, ProfileChange.Edit)) return;
            var changed = Profiles.ReplaceChoices(existing.Id, CurrentChoices());
            if (changed is not null) Scraping.Subscriptions.SyncProfile(changed);
            clean = existing.Name;
        }
        else
        {
            Profiles.Add(CurrentChoices() with { Name = clean });
        }
        RebuildProfileChoices();
        StatusText = L.Format("msg_prof_saved", clean);
    }

    private bool ManagedIsUsable(out FormatProfile profile)
    {
        profile = null!;
        if (SelectedManagedProfile is not { } row)
        {
            _ = _ui.ShowMessage(L["msg_prof_noselect"]);
            return false;
        }
        if (row.BuiltIn)
        {
            _ = _ui.ShowMessage(L["msg_prof_builtin"]);
            return false;
        }
        profile = row.Profile;
        return true;
    }

    [RelayCommand]
    private async Task RenameProfile()
    {
        if (!ManagedIsUsable(out var p)) return;
        var name = await _ui.AskText(L["dlg_prof_rename"], L["lbl_prof_name"], p.Name);
        if (name is null) return;
        var clean = FormatProfile.CleanName(name);
        if (clean.Length == 0)
        {
            await _ui.ShowMessage(L["msg_prof_empty"], warning: true);
            return;
        }
        if (Profiles.NameTaken(clean, p.Id, ReservedProfileNames()))
        {
            await _ui.ShowMessage(L["msg_prof_taken"], warning: true);
            return;
        }
        if (clean == p.Name || !await ConfirmProfileChangeAsync(p, ProfileChange.Rename)) return;
        Profiles.Rename(p.Id, clean);
        RebuildProfileChoices();
    }

    [RelayCommand]
    private async Task DeleteProfile()
    {
        if (!ManagedIsUsable(out var p)) return;
        // With subscriptions involved the warning (which names them) is the question; otherwise a plain "delete?".
        var affected = Scraping.Subscriptions.NamesUsing(p.Id).Count > 0;
        if (affected ? !await ConfirmProfileChangeAsync(p, ProfileChange.Delete) : !await _ui.AskYesNo(L["grp_profiles"], L.Format("msg_prof_delete", p.Name))) return;
        Scraping.Subscriptions.DetachProfile(p.Id);
        Profiles.Remove(p.Id);
        RebuildProfileChoices();
    }

    [RelayCommand]
    private async Task ExportProfiles()
    {
        if (Profiles.User.Count == 0)
        {
            await _ui.ShowMessage(L["msg_prof_none_own"]);
            return;
        }
        var path = await _ui.PickSaveFileAs("videoprep-perfiles.json", "JSON", ["*.json"]);
        if (path is null) return;
        try
        {
            await File.WriteAllTextAsync(path, ProfileStore.Export(Profiles.User), new System.Text.UTF8Encoding(false));
            await _ui.ShowMessage(L["msg_prof_exported"]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _svc.Log.Write("WARN  could not export the profiles: " + ex.Message);
            await _ui.ShowMessage(L["msg_prof_import_bad"], warning: true);
        }
    }

    [RelayCommand]
    private async Task ImportProfiles()
    {
        var path = await _ui.PickOpenFileAs("JSON", ["*.json"]);
        if (path is null) return;
        var result = Profiles.Import(path, ReservedProfileNames());
        if (result.Problem is not null)
        {
            _svc.Log.Write("Profiles import: " + result.Problem);
            await _ui.ShowMessage(L["msg_prof_import_bad"], warning: true);
            return;
        }
        RebuildProfileChoices();
        await _ui.ShowMessage(L.Format("msg_prof_import_ok", result.Added.Count) + (result.Skipped > 0 ? L.Format("msg_prof_import_skipped", result.Skipped) : ""));
    }
}
