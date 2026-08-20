using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace ClipCanvasMirror;

public partial class RegionSelectorWindow : Window
{
    private System.Windows.Point _startVisual;
    private NativePoint _startScreen;
    private bool _dragging;

    public CaptureRegion? SelectedRegion { get; private set; }

    public RegionSelectorWindow()
    {
        InitializeComponent();
        SourceInitialized += Window_SourceInitialized;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        // SystemParametersの値はWPF論理単位のため、DPI倍率が異なる複数画面では
        // 仮想デスクトップの右端・下端まで届かない場合がある。
        // Win32の物理ピクセル座標で選択ウィンドウを全画面に配置する。
        var left = GetSystemMetrics(SystemMetricXVirtualScreen);
        var top = GetSystemMetrics(SystemMetricYVirtualScreen);
        var width = GetSystemMetrics(SystemMetricCxVirtualScreen);
        var height = GetSystemMetrics(SystemMetricCyVirtualScreen);
        var handle = new WindowInteropHelper(this).Handle;

        if (!SetWindowPos(handle, TopmostHandle, left, top, width, height,
                SetWindowPosNoActivate | SetWindowPosShowWindow))
        {
            // 万一Win32配置に失敗した場合は、従来のWPF座標をフォールバックに使う。
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startVisual = e.GetPosition(SelectionCanvas);
        GetCursorPos(out _startScreen);
        _dragging = true;
        CaptureMouse();
        SelectionBorder.Visibility = Visibility.Visible;
        UpdateSelection(_startVisual);
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging)
            UpdateSelection(e.GetPosition(SelectionCanvas));
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;
        ReleaseMouseCapture();
        GetCursorPos(out var endScreen);

        var left = Math.Min(_startScreen.X, endScreen.X);
        var top = Math.Min(_startScreen.Y, endScreen.Y);
        var width = Math.Abs(endScreen.X - _startScreen.X);
        var height = Math.Abs(endScreen.Y - _startScreen.Y);

        if (width >= 10 && height >= 10)
        {
            SelectedRegion = new CaptureRegion(left, top, width, height);
            DialogResult = true;
        }
        else
        {
            SelectionBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateSelection(System.Windows.Point current)
    {
        var left = Math.Min(_startVisual.X, current.X);
        var top = Math.Min(_startVisual.Y, current.Y);
        var width = Math.Abs(current.X - _startVisual.X);
        var height = Math.Abs(current.Y - _startVisual.Y);
        Canvas.SetLeft(SelectionBorder, left);
        Canvas.SetTop(SelectionBorder, top);
        SelectionBorder.Width = width;
        SelectionBorder.Height = height;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            DialogResult = false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    private const int SystemMetricXVirtualScreen = 76;
    private const int SystemMetricYVirtualScreen = 77;
    private const int SystemMetricCxVirtualScreen = 78;
    private const int SystemMetricCyVirtualScreen = 79;
    private const uint SetWindowPosNoActivate = 0x0010;
    private const uint SetWindowPosShowWindow = 0x0040;
    private static readonly IntPtr TopmostHandle = new(-1);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}

