using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Miniprep.App.Views;

/// <summary>The two small windows miniprep needs (a message and a line of text), built in code with the theme resources.</summary>
internal static class Dialogs
{
    private static Window Shell(string title, Control content, double width) => new()
    {
        Title = title,
        Content = new Border { Padding = new Thickness(18), Child = content },
        Width = width,
        SizeToContent = SizeToContent.Height,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        ShowInTaskbar = false,
    };

    private static Button Btn(string text, string? cls = null)
    {
        var b = new Button { Content = text, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (cls is not null) b.Classes.Add(cls);
        return b;
    }

    private static StackPanel Buttons(params Button[] buttons)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var b in buttons) p.Children.Add(b);
        return p;
    }

    public static async Task Message(Window owner, string text)
    {
        var ok = Btn(Loc.Instance["btn_ok"], "o");
        var panel = new StackPanel { Spacing = 16, Children = { new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 420 }, Buttons(ok) } };
        var w = Shell("miniprep", panel, 460);
        ok.Click += (_, _) => w.Close();
        await w.ShowDialog(owner);
    }

    public static async Task<string?> Text(Window owner, string title, string prompt, string initial)
    {
        var box = new TextBox { Text = initial, MinWidth = 340 };
        var ok = Btn(Loc.Instance["btn_ok"], "g");
        var cancel = Btn(Loc.Instance["btn_cancel"]);
        var panel = new StackPanel { Spacing = 12, Children = { new TextBlock { Text = prompt }, box, Buttons(ok, cancel) } };
        var w = Shell(title, panel, 440);
        ok.Click += (_, _) => w.Close(box.Text ?? "");
        cancel.Click += (_, _) => w.Close(null);
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; w.Close(box.Text ?? ""); }
        };
        w.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        return await w.ShowDialog<string?>(owner);
    }
}
