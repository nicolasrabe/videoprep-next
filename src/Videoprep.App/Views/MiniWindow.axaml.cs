using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Videoprep.App.Services;
using Videoprep.App.ViewModels;

namespace Videoprep.App.Views;

/// <summary>
/// The mini window (F4). Rules that matter more than looks:
/// it never takes the keyboard focus (shown without activating, plus the "do not activate" window style on Windows);
/// it can be dragged from anywhere; the right-click menu opens and closes (Esc, click outside) even though a window
/// that is never activated gets no keys of its own, and closing the menu leaves the focus where the user was typing.
/// </summary>
public partial class MiniWindow : Window
{
    private bool _closingForReal;
    private DispatcherTimer? _menuWatch;
    private IntPtr _focusBeforeMenu;

    public MiniWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Platform.MakeNoActivate(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
        PointerPressed += OnPointerPressed;
        PositionChanged += (_, e) => (DataContext as MainViewModel)?.SetMiniPosition(e.Point.X, e.Point.Y);
        Closing += (_, _) =>
        {
            // Closed by something other than "Cerrar modo mini" (the system, a script): the big window must come back.
            if (_closingForReal) return;
            _closingForReal = true;
            (DataContext as MainViewModel)?.CloseMini();
        };
        Menu.Opened += (_, _) => OnMenuOpened();
        Menu.Closed += (_, _) => OnMenuClosed();
    }

    public void CloseForReal()
    {
        _closingForReal = true;
        StopMenuWatch();
        Close();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        // Buttons are buttons; anywhere else drags the window.
        if (e.Source is Visual v && (v.FindAncestorOfType<Button>(includeSelf: true) is not null || v.FindAncestorOfType<ToggleButton>(includeSelf: true) is not null)) return;
        try { BeginMoveDrag(e); }
        catch (InvalidOperationException) { /* the pointer was released already */ }
    }

    // ---- right-click menu without keyboard focus

    private void OnMenuOpened()
    {
        _focusBeforeMenu = Platform.ForegroundWindow();
        StartMenuWatch();
    }

    private void OnMenuClosed()
    {
        StopMenuWatch();
        // The menu or the click may have pulled the focus to videoprep: give it back to the program the user was typing in.
        var now = Platform.ForegroundWindow();
        if (_focusBeforeMenu != IntPtr.Zero && !Platform.IsOwnWindow(_focusBeforeMenu) && Platform.IsOwnWindow(now))
            Platform.RestoreForeground(_focusBeforeMenu);
    }

    /// <summary>
    /// A window that is never activated receives no Esc key and no "click outside" signal from Windows, so while the menu
    /// is open videoprep looks at the keyboard and mouse itself every 30 ms: Esc, or a click anywhere outside the menu,
    /// closes it.
    /// </summary>
    private void StartMenuWatch()
    {
        StopMenuWatch();
        var wasDown = Platform.IsKeyDown(Platform.VkLeftButton) || Platform.IsKeyDown(Platform.VkRightButton);
        _menuWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _menuWatch.Tick += (_, _) =>
        {
            if (Platform.IsKeyDown(Platform.VkEscape))
            {
                Menu.Close();
                return;
            }
            var down = Platform.IsKeyDown(Platform.VkLeftButton) || Platform.IsKeyDown(Platform.VkRightButton);
            if (down && !wasDown && !CursorOverMenu()) Menu.Close();
            wasDown = down;
        };
        _menuWatch.Start();
    }

    private void StopMenuWatch()
    {
        _menuWatch?.Stop();
        _menuWatch = null;
    }

    private bool CursorOverMenu()
    {
        if (Platform.CursorPosition() is not { } cur) return false;
        if (TopLevel.GetTopLevel(Menu) is not { } root || ReferenceEquals(root, this)) return false;
        var origin = root.PointToScreen(new Point(0, 0));
        var scale = root.RenderScaling;
        var w = root.Bounds.Width * scale;
        var h = root.Bounds.Height * scale;
        return cur.X >= origin.X && cur.X < origin.X + w && cur.Y >= origin.Y && cur.Y < origin.Y + h;
    }
}
