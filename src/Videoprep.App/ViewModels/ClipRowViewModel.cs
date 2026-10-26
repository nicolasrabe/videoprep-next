using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Videoprep.Core.Text;

namespace Videoprep.App.ViewModels;

/// <summary>One clip row in the Download tab: start, end, and the X button. Invalid text turns red (legacy Test-ClipText).</summary>
public sealed partial class ClipRowViewModel(Action<ClipRowViewModel> remove) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartValid))]
    private string _start = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndValid))]
    private string _end = "";

    public bool StartValid => IsOk(Start);
    public bool EndValid => IsOk(End);
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
