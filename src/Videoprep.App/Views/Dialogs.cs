using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;

namespace Videoprep.App.Views;

/// <summary>
/// Small modal windows built in code: message (OK), question (Yes/No), color (#rrggbb) and Options.
/// Avalonia has no message box of its own; these follow the current theme through the same resources.
/// </summary>
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

    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };

    private static Button Btn(string text, string? cls = null)
    {
        var b = new Button { Content = text, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        if (cls is not null) b.Classes.Add(cls);
        return b;
    }

    public static async Task Message(Window owner, string text, bool warning)
    {
        var ok = Btn(Loc.Instance["btn_ok"], warning ? "o" : null);
        var panel = new StackPanel { Spacing = 16, Children = { Text(text), Buttons(ok) } };
        var w = Shell("videoprep", panel, 480);
        ok.Click += (_, _) => w.Close();
        await w.ShowDialog(owner);
    }

    public static async Task<bool> YesNo(Window owner, string title, string text)
    {
        // Spanish "Sí"/"No", English "Yes"/"No" (the legacy used the system message box buttons).
        var yes = Btn(Loc.Instance.Language == "es" ? "Sí" : "Yes", "g");
        var no = Btn("No");
        var panel = new StackPanel { Spacing = 16, Children = { Text(text), Buttons(yes, no) } };
        var w = Shell(title, panel, 520);
        yes.Click += (_, _) => w.Close(true);
        no.Click += (_, _) => w.Close(false);
        return await w.ShowDialog<bool>(owner);
    }

    /// <summary>A question with the buttons named by the caller ("Continuar" / "Cancelar"); true = the first.</summary>
    public static async Task<bool> Choice(Window owner, string title, string text, string yes, string no)
    {
        var go = Btn(yes, "o");
        var stop = Btn(no);
        var panel = new StackPanel { Spacing = 16, Children = { Text(text), Buttons(go, stop) } };
        var w = Shell(title, panel, 520);
        go.Click += (_, _) => w.Close(true);
        stop.Click += (_, _) => w.Close(false);
        return await w.ShowDialog<bool>(owner);
    }

    /// <summary>One line of text asked from the user; Enter accepts, null = Cancelar.</summary>
    public static async Task<string?> Text(Window owner, string title, string prompt, string initial)
    {
        var box = new TextBox { Text = initial, MinWidth = 320 };
        var ok = Btn(Loc.Instance["btn_ok"], "g");
        var cancel = Btn(Loc.Instance["btn_cancel"]);
        var panel = new StackPanel { Spacing = 12, Children = { Text(prompt), box, Buttons(ok, cancel) } };
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

    public static async Task<string?> Color(Window owner, string current)
    {
        var box = new TextBox { Text = current, Width = 140 };
        var swatch = new Border { Width = 36, Height = 28, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray };
        void Paint()
        {
            try { swatch.Background = new SolidColorBrush(Avalonia.Media.Color.Parse(box.Text ?? "")); }
            catch (FormatException) { swatch.Background = Brushes.Transparent; }
        }
        box.TextChanged += (_, _) => Paint();
        Paint();
        var ok = Btn(Loc.Instance["btn_ok"], "g");
        var cancel = Btn(Loc.Instance["btn_cancel"]);
        var panel = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Text(Loc.Instance["lbl_color_hex"]),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { box, swatch } },
                Buttons(ok, cancel),
            },
        };
        var w = Shell(Loc.Instance["dlg_color"], panel, 400);
        ok.Click += (_, _) => w.Close(box.Text);
        cancel.Click += (_, _) => w.Close(null);
        return await w.ShowDialog<string?>(owner);
    }

    public static async Task<bool> Options(Window owner, OptionsViewModel vm)
    {
        var view = new OptionsView { DataContext = vm };
        var ok = Btn(Loc.Instance["btn_ok"], "g");
        var cancel = Btn(Loc.Instance["btn_cancel"]);
        var panel = new StackPanel { Spacing = 16, Children = { view, Buttons(ok, cancel) } };
        var w = Shell(Loc.Instance["dlg_options"], panel, 600);
        ok.Click += (_, _) => w.Close(true);
        cancel.Click += (_, _) => w.Close(false);
        return await w.ShowDialog<bool>(owner);
    }

    /// <summary>Agregar/Editar suscripción. Aceptar only closes when the data is valid (else it says what is wrong).</summary>
    public static async Task<bool> Subscription(Window owner, SubscriptionEditViewModel vm)
    {
        var view = new SubscriptionEditView { DataContext = vm };
        var ok = Btn(Loc.Instance["btn_ok"], "g");
        var cancel = Btn(Loc.Instance["btn_cancel"]);
        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 16 };
        grid.Children.Add(new ScrollViewer { Content = view, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        var buttons = Buttons(ok, cancel);
        Grid.SetRow(buttons, 1);
        grid.Children.Add(buttons);
        var w = Shell(vm.Title, grid, 780);
        // Big form: the user can resize it; it starts at most 700 px high and scrolls.
        w.CanResize = true;
        w.SizeToContent = SizeToContent.Manual;
        w.Height = Math.Min(700, owner.Bounds.Height > 0 ? owner.Bounds.Height : 700);
        w.MinWidth = 600;
        w.MinHeight = 400;
        ok.Click += async (_, _) =>
        {
            if (vm.Build(out var problem) is not null) w.Close(true);
            else await Message(w, problem!, warning: true);
        };
        cancel.Click += (_, _) => w.Close(false);
        return await w.ShowDialog<bool>(owner);
    }

    /// <summary>"Enlaces encontrados en ..." (F1): a big resizable window; true = "Agregar N a la cola".</summary>
    public static async Task<bool> ImportReview(Window owner, ImportReviewViewModel vm)
    {
        var view = new ImportReviewView { DataContext = vm };
        var w = Shell(vm.Title, view, 980);
        w.CanResize = true;
        w.SizeToContent = SizeToContent.Manual;
        w.Height = Math.Min(620, owner.Bounds.Height > 0 ? owner.Bounds.Height : 620);
        w.MinWidth = 760;
        w.MinHeight = 380;
        view.AddButton.Click += (_, _) => w.Close(true);
        view.CancelButton.Click += (_, _) => w.Close(false);
        return await w.ShowDialog<bool>(owner);
    }

    /// <summary>"Cambiar N elementos de la cola" (F2); true = "Aplicar a los N".</summary>
    public static async Task<bool> BatchEdit(Window owner, BatchEditViewModel vm)
    {
        var view = new BatchEditView { DataContext = vm };
        var apply = Btn(vm.ApplyText, "o");
        var cancel = Btn(Loc.Instance["btn_cancel"]);
        var panel = new StackPanel { Spacing = 16, Children = { view, Buttons(cancel, apply) } };
        var w = Shell(vm.Title, panel, 640);
        apply.Click += (_, _) => w.Close(true);
        cancel.Click += (_, _) => w.Close(false);
        return await w.ShowDialog<bool>(owner);
    }

    private static StackPanel Buttons(params Button[] buttons)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var b in buttons) p.Children.Add(b);
        return p;
    }
}
