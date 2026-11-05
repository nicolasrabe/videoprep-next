using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Miniprep.App.Logic;
using Miniprep.App.ViewModels;
using Miniprep.App.Views;
using Videoprep.Core.Preview;

namespace Miniprep.App.Tests;

/// <summary>The two tabs rendered in a real window without a screen (PNG files in bin/.../screenshots/), and a check that no raw text key shows.</summary>
public sealed partial class ScreensTests : IDisposable
{
    private readonly MiniRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [GeneratedRegex("^[a-z]{1,8}_[a-zA-Z0-9_]+$")]
    private static partial Regex LooksLikeKey();

    private static Bitmap Picture(int w, int h, Color from, Color to)
    {
        var box = new Border
        {
            Width = w, Height = h,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = [new GradientStop(from, 0), new GradientStop(to, 1)],
            },
        };
        box.Measure(new Size(w, h));
        box.Arrange(new Rect(0, 0, w, h));
        var bmp = new RenderTargetBitmap(new PixelSize(w, h));
        bmp.Render(box);
        return bmp;
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

    private (MainWindow Window, MiniMainViewModel Vm) Open(string lang)
    {
        Loc.Instance.Language = lang;
        var vm = new MiniMainViewModel(_rig.Services, _rig.Ui, _rig.Downloader, null, post: a => Dispatcher.UIThread.Post(a), delay: (_, _) => Task.CompletedTask);
        var window = new MainWindow { Width = 620, Height = 700, DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 300 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.True(done());
    }

    [AvaloniaFact]
    public async Task Prep_empty_is_only_the_box_and_Prep_ready_shows_the_preview()
    {
        var (window, vm) = Open("es");
        var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.DoesNotContain(texts, t => t == "Cargando vista previa...");
        Assert.DoesNotContain(texts, t => (t ?? "").Contains("enlace", StringComparison.OrdinalIgnoreCase) && t != "Pegá acá el enlace del video");   // no help texts
        Save(window, "miniprep-prep-vacio-es");

        vm.Prep.Url = "https://www.youtube.com/watch?v=Qw3r7yTz9Aa";
        await Until(() => vm.Prep.IsReady);
        vm.Prep.Thumb = Picture(640, 360, Color.Parse("#3c6f9e"), Color.Parse("#222222"));
        Dispatcher.UIThread.RunJobs();
        NoRawKeys(window);
        Assert.Equal("Entrevista completa", window.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible && t.FontSize == 16).Text);
        Save(window, "miniprep-prep-listo-es");
    }

    [AvaloniaFact]
    public void Cola_shows_the_rows_with_their_states()
    {
        var (window, vm) = Open("es");
        VideoInfo Info(string key, string title, string channel, string duration) =>
            new() { Key = key, Url = "u", State = InfoState.Ready, Title = title, Channel = channel, Duration = duration, Description = "En este episodio charlamos con nuestro invitado sobre cómo empezó su proyecto…" };
        var a = vm.Queue.Add("https://www.youtube.com/watch?v=aaaaaaaaaaa", Info("a", "Big Buck Bunny 60fps 4K", "Blender", "10:34")).Id;
        vm.Queue.Add("https://www.youtube.com/watch?v=bbbbbbbbbbb", Info("b", "Entrevista al invitado - parte 1", "Canal de ejemplo", "38:12"));
        var c = vm.Queue.Add("https://www.youtube.com/watch?v=ccccccccccc", Info("c", "Me at the zoo", "jawed", "0:19")).Id;
        var d = vm.Queue.Add("https://www.youtube.com/watch?v=ddddddddddd", Info("d", "Clip de la transmisión", "Canal de ejemplo", "0:45")).Id;
        vm.Queue.SetQuality(c, Quality.Best);
        vm.Queue.SetStatus(a, EntryStatus.Downloading);
        vm.Queue.SetStatus(d, EntryStatus.Done);
        Dispatcher.UIThread.RunJobs();
        vm.Cola.Rows.First(r => r.Id == a).Progress = 37;
        foreach (var r in vm.Cola.Rows) r.Thumb = Picture(168, 94, Color.Parse("#3c6f9e"), Color.Parse("#222222"));
        vm.Cola.ToggleOpen(vm.Cola.Rows[1]);
        vm.SelectedTab = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("2 en cola · 1 listo", vm.Cola.Counter);
        Assert.Equal("3", window.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible && t.FontSize == 11).Text);   // the number on the tab: waiting + downloading
        NoRawKeys(window);
        Save(window, "miniprep-cola-es");
    }

    [AvaloniaFact]
    public void The_empty_queue_says_what_to_do()
    {
        var (window, vm) = Open("es");
        vm.SelectedTab = 1;
        Dispatcher.UIThread.RunJobs();
        var texts = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToList();
        Assert.Contains(texts, t => t.StartsWith("La cola está vacía."));
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.FontSize == 11);   // no number on the tab
        NoRawKeys(window);
    }

    [AvaloniaFact]
    public void English_follows_the_language()
    {
        var (window, vm) = Open("en");
        vm.Prep.Url = "";
        var texts = window.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).ToList();
        Assert.Contains("Add to queue", texts);
        Assert.Contains("Download...", texts);
        Assert.Contains("Paste", texts);
        NoRawKeys(window);
        Loc.Instance.Language = "es";
    }
}
