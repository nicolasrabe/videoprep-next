using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;
using Videoprep.App.Views;
using Videoprep.Core.Import;
using Videoprep.Core.Storage;
using Videoprep.Core.Tests;
using Videoprep.Core.Themes;

namespace Videoprep.App.Tests;

/// <summary>
/// The new 0.3 screens in the real window, rendered without a screen: the import review, "Cambiar N elementos", an
/// open queue row and the fragment warning, in every theme and both languages (PNG files in bin/.../screenshots/).
/// </summary>
public sealed partial class QueueScreensTests : IDisposable
{
    private readonly TestRig _rig = new(tools: true);

    public void Dispose() => _rig.Dispose();

    [GeneratedRegex("^[a-z]{1,8}_[a-zA-Z0-9_]+$")]
    private static partial Regex LooksLikeKey();

    public static TheoryData<string, string> Combos()
    {
        var data = new TheoryData<string, string>();
        foreach (var lang in new[] { "es", "en" })
            foreach (var theme in new[] { "light", "dark", "beige", "gray", "slate", "custom" })
                data.Add(lang, theme);
        return data;
    }

    private (MainWindow Window, MainViewModel Vm) Open(string lang, string theme)
    {
        Loc.Instance.Language = lang;
        SettingsStore.Save(_rig.Paths.SettingsFile, new Settings { Lang = lang });
        var window = new MainWindow { Width = 1000, Height = 740 };
        var vm = new MainViewModel(_rig.Services, window, a => Dispatcher.UIThread.Post(a));
        vm.Settings.CustomColor = "#3a2f4a";
        vm.Settings.Theme = theme;
        vm.Folder = _rig.OutFolder;
        ThemeService.Apply(Application.Current!, Palette.For(theme, "#3a2f4a"));
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static void Save(TopLevel window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        frame!.Save(Path.Combine(dir, name + ".png"), PngBitmapEncoderOptions.Default);
    }

    private static void NoRawKeys(Visual root)
    {
        var texts = root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();
        Assert.DoesNotContain(texts, t => LooksLikeKey().IsMatch(t));
        Assert.DoesNotContain(texts, t => t.Contains("{0}", StringComparison.Ordinal));
    }

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 300 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(30);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.True(done(), "timed out");
    }

    private string Script()
    {
        var d = new DocxBuilder();
        d.Body($"<w:p>{DocxBuilder.Run("Escena del conejo (F1): ")}{d.Hyperlink("https://youtu.be/aqz-KE-bpKQ", "Escena del conejo (F1)")}</w:p>");
        d.Body($"<w:p>{DocxBuilder.Run("Para el final ")}{d.Hyperlink("https://youtu.be/jNQXAC9IVRw", "mirar este clip")}</w:p>");
        d.Para("Plano general: https://www.youtube.com/watch?v=Qw3r7yTz9Aa en la mitad");
        d.Body($"<w:p>{d.Hyperlink("https://www.twitch.tv/ejemplo/clip/AbCdEf", "clip de la transmisión")}</w:p>");
        d.Para("Reacción: x.com/usuario/status/1234567890");
        d.Body($"<w:p>{d.Hyperlink("https://youtu.be/jNQXAC9IVRw", "otra vez el mismo clip")}</w:p>");
        d.Para("Canal: https://www.youtube.com/@BlenderStudio");
        d.Body($"<w:p>{d.Hyperlink("https://miempresa.com/informe.pdf", "informe del cliente")}</w:p>");
        return d.Save(_rig.Dir, "guion-video-23.docx");
    }

    [AvaloniaTheory]
    [MemberData(nameof(Combos))]
    public async Task New_screens_in_every_theme_and_language(string lang, string theme)
    {
        var (w, vm) = Open(lang, theme);

        // 1. "Enlaces encontrados en ..." (F1)
        var found = LinkFileReader.Read(Script());
        var review = new ImportReviewViewModel("guion-video-23.docx", ImportPlanner.Plan(found.Links, []), "mp4", 1080);
        review.Rows[0].Name = "F1";
        var reviewWindow = new Window { Width = 980, Height = 560, Content = new Border { Padding = new Thickness(18), Child = new ImportReviewView { DataContext = review } } };
        reviewWindow.Show();
        Save(reviewWindow, $"v03-{lang}-{theme}-importar");
        NoRawKeys(reviewWindow);
        reviewWindow.Close();

        // 2. "Cambiar N elementos de la cola" (F2)
        vm.SmartPaste("https://fake.test/ok/a1\nhttps://fake.test/ok/a2\nhttps://fake.test/ok/a3");
        vm.Rename(vm.QueueRows[0], lang == "es" ? "Entrevista - parte 1" : "Interview - part 1");
        var batch = new BatchEditViewModel(vm.Items.Take(3).ToList(), 0, _ => null, _ => 1);
        batch.SelectedNames = batch.NameModes[1];
        batch.Prefix = lang == "es" ? "Escena" : "Scene";
        var batchWindow = new Window { Width = 640, Height = 460, Content = new Border { Padding = new Thickness(18), Child = new BatchEditView { DataContext = batch } } };
        batchWindow.Show();
        Save(batchWindow, $"v03-{lang}-{theme}-cambiar-varios");
        NoRawKeys(batchWindow);
        batchWindow.Close();

        // 3. Queue with one open row (F3)
        vm.SelectedTab = MainViewModel.TabQueue;
        Dispatcher.UIThread.RunJobs();
        var grid = w.GetVisualDescendants().OfType<DataGrid>().First(g => g.ItemsSource == vm.QueueRows);
        grid.SelectedItem = vm.QueueRows[1];
        await Until(() => vm.QueueRows[1].Card is { IsReady: true });
        Save(w, $"v03-{lang}-{theme}-cola-fila-abierta");
        NoRawKeys(w);

        // 4. Fragment beyond the video duration (F2)
        vm.SelectedTab = MainViewModel.TabDownload;
        vm.SmartPaste("https://fake.test/ok/d1");
        vm.AddClipCommand.Execute(null);
        vm.Clips[0].Start = "00:00:10:00";
        vm.Clips[0].End = "00:05:00:00";
        await Until(() => vm.Clips[0].HasWarning);
        Save(w, $"v03-{lang}-{theme}-fragmento-fuera");
        Assert.Contains(lang == "es" ? "El video dura 4:05" : "The video is 4:05 long", vm.Clips[0].Warning);
    }

    [AvaloniaFact]
    public async Task Clicking_rows_opens_one_at_a_time_and_F2_renames_in_the_real_table()
    {
        var (w, vm) = Open("es", "light");
        vm.SmartPaste("https://fake.test/ok/r1\nhttps://fake.test/ok/r2\nhttps://fake.test/ok/r3");
        vm.SelectedTab = MainViewModel.TabQueue;
        Dispatcher.UIThread.RunJobs();
        var grid = w.GetVisualDescendants().OfType<DataGrid>().First(g => g.ItemsSource == vm.QueueRows);

        DataGridRow RowOf(int i) => grid.GetVisualDescendants().OfType<DataGridRow>().First(r => r.DataContext == vm.QueueRows[i]);
        Point Center(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, 12), w)!.Value;

        // Click the first row: it opens and its details are shown.
        var p0 = Center(RowOf(0));
        w.MouseDown(p0, MouseButton.Left);
        w.MouseUp(p0, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.QueueRows[0].IsExpanded);
        Assert.True(RowOf(0).AreDetailsVisible);
        await Until(() => vm.QueueRows[0].Card is { IsReady: true });

        // Arrow down: the next row opens, the first one closes.
        w.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        w.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(vm.QueueRows[1], grid.SelectedItem);
        Assert.False(RowOf(0).AreDetailsVisible);
        Assert.True(RowOf(1).AreDetailsVisible);

        // A click on the open row closes it.
        var p1 = Center(RowOf(1));
        w.MouseDown(p1, MouseButton.Left);
        w.MouseUp(p1, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.QueueRows[1].IsExpanded);
        Assert.False(RowOf(1).AreDetailsVisible);

        // F2: rename in the row; Enter confirms.
        w.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
        Dispatcher.UIThread.RunJobs();
        var box = RowOf(1).GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        Assert.NotNull(box);
        box!.Text = "Plano general";
        w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Plano general", vm.Items[1].Custom);
        Assert.Equal("Plano general", vm.QueueRows[1].Name);
        Assert.Equal(vm.QueueRows[1], grid.SelectedItem);          // Enter stays on the row
        Assert.False(vm.QueueRows[2].IsExpanded);
        Save(w, "v03-es-light-renombrado");

        // Escape cancels.
        grid.SelectedItem = vm.QueueRows[2];
        Dispatcher.UIThread.RunJobs();
        w.KeyPress(Key.F2, RawInputModifiers.None, PhysicalKey.F2, null);
        Dispatcher.UIThread.RunJobs();
        RowOf(2).GetVisualDescendants().OfType<TextBox>().First().Text = "no";
        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Core.Models.NamingMode.Original, vm.Items[2].Naming);

        // Several rows selected: none is open.
        grid.SelectAll();
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(vm.QueueRows, r => r.IsExpanded);

        // Ctrl+Z undoes the rename.
        w.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Core.Models.NamingMode.Original, vm.Items[1].Naming);
    }

    [AvaloniaFact]
    public void Right_click_menu_has_every_queue_action()
    {
        var (w, vm) = Open("es", "light");
        vm.SelectedTab = MainViewModel.TabQueue;
        Dispatcher.UIThread.RunJobs();
        var grid = w.GetVisualDescendants().OfType<DataGrid>().First(g => g.ItemsSource == vm.QueueRows);
        var items = grid.ContextMenu!.Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();
        Assert.Equal(
            ["Renombrar (F2)", "Cambiar formato o nombres de los seleccionados...", "Editar en la pestaña Descargar", "Duplicar", "Subir", "Bajar",
             "Volver a descargar", "Mostrar en carpeta", "Quitar (Supr)"],
            items);
        Assert.Equal("Cargar archivo...", w.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).First(t => t?.StartsWith("Cargar", StringComparison.Ordinal) == true));
    }
}
