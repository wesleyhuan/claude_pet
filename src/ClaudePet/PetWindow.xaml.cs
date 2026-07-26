using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClaudePet.Models;
using ClaudePet.Native;
using ClaudePet.Rendering;
using ClaudePet.Settings;

namespace ClaudePet;

public partial class PetWindow : Window
{
    private readonly DispatcherTimer _animationTimer;
    private readonly SettingsStore _settingsStore;
    private IReadOnlyList<PixelFrame> _currentFrames = PixelArtGenerator.GenerateFrames(Mood.NoSession);
    private int _frameIndex;
    private bool _dragMode;

    public PetWindow(SettingsStore settingsStore)
    {
        InitializeComponent();
        _settingsStore = settingsStore;

        var settings = _settingsStore.Load();
        if (settings.WindowLeft >= 0 && settings.WindowTop >= 0)
        {
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
        }
        else
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 16;
            Top = workArea.Bottom - Height - 16;
        }

        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _animationTimer.Tick += (_, _) => AdvanceFrame();
        _animationTimer.Start();

        SourceInitialized += (_, _) => ApplyClickThrough(!_dragMode);
        MouseLeftButtonDown += (_, _) => { if (_dragMode) DragMove(); };
        Closing += (_, _) => SavePosition();

        Render();
    }

    public void SetMood(Mood mood)
    {
        _currentFrames = PixelArtGenerator.GenerateFrames(mood);
        _frameIndex = 0;
        Render();
    }

    public void SetDragMode(bool enabled)
    {
        _dragMode = enabled;
        ApplyClickThrough(!enabled);
    }

    private void AdvanceFrame()
    {
        _frameIndex = (_frameIndex + 1) % _currentFrames.Count;
        Render();
    }

    private void Render()
    {
        var frame = _currentFrames[_frameIndex];
        var bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
        // uint[] holds 0xAARRGGBB per pixel; on little-endian Windows this is
        // byte-identical to the B,G,R,A layout WritePixels expects for Bgra32.
        bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Pixels, frame.Width * 4, 0);
        SpriteImage.Source = bitmap;
    }

    private void ApplyClickThrough(bool clickThrough)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            ClickThroughHelper.SetClickThrough(hwnd, clickThrough);
    }

    private void SavePosition()
    {
        var settings = _settingsStore.Load() with { WindowLeft = Left, WindowTop = Top };
        _settingsStore.Save(settings);
    }
}
