using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ClipCanvasMirror;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _timer = new();
    private AppSettings _settings = new();
    private bool _loaded;
    private bool _captureBusy;
    private readonly Dictionary<Image, PreviewViewState> _previewViewStates = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += Window_Loaded;
        Closing += Window_Closing;
        _timer.Tick += Capture_Tick;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = SettingsStore.Load();
        RestoreWindowBounds();

        LayoutComboBox.SelectedIndex = _settings.VerticalLayout ? 1 : 0;
        FpsComboBox.SelectedIndex = FpsToIndex(_settings.Fps);
        AnalysisModeComboBox.SelectedIndex = AnalysisModeToIndex(_settings.AnalysisMode);
        TopmostCheckBox.IsChecked = _settings.Topmost;
        ShowMirrorCheckBox.IsChecked = _settings.ShowMirror;
        ShowGrayCheckBox.IsChecked = _settings.ShowGray;
        Topmost = _settings.Topmost;
        if (!_settings.ShowMirror && !_settings.ShowGray)
        {
            _settings.ShowMirror = true;
            ShowMirrorCheckBox.IsChecked = true;
        }
        ApplyPreviewLayout();
        UpdateAnalysisLabel();
        ApplySettingsCollapsed();
        ApplyTimerInterval();
        _loaded = true;

        if (_settings.CaptureRegion.IsValid)
        {
            StatusText.Text = RegionStatus();
            _timer.Start();
        }
    }

    private async void SelectRegion_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        Hide();
        await Task.Delay(180);

        var selector = new RegionSelectorWindow();
        var accepted = selector.ShowDialog() == true;
        Show();
        Activate();

        if (accepted && selector.SelectedRegion is { IsValid: true } region)
        {
            _settings.CaptureRegion = region;
            SettingsStore.Save(_settings);
            StatusText.Text = RegionStatus();
        }

        if (_settings.CaptureRegion.IsValid && PauseButton.IsChecked != true)
            _timer.Start();
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (PauseButton.IsChecked == true)
        {
            _timer.Stop();
            PauseButton.Content = "再開";
            StatusText.Text = "一時停止中";
        }
        else
        {
            PauseButton.Content = "一時停止";
            if (_settings.CaptureRegion.IsValid)
            {
                StatusText.Text = RegionStatus();
                _timer.Start();
            }
        }
    }

    private void Layout_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (LayoutComboBox.SelectedItem is not ComboBoxItem item)
            return;

        var vertical = Equals(item.Tag, "Vertical");
        _settings.VerticalLayout = vertical;
        ApplyPreviewLayout();
        SaveIfLoaded();
    }

    private void ApplyPreviewLayout()
    {
        var vertical = _settings.VerticalLayout;
        var showMirror = _settings.ShowMirror;
        var showGray = _settings.ShowGray;
        MirrorPanel.Visibility = showMirror ? Visibility.Visible : Visibility.Collapsed;
        GrayPanel.Visibility = showGray ? Visibility.Visible : Visibility.Collapsed;
        SwapButton.IsEnabled = showMirror && showGray;

        PreviewGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        PreviewGrid.ColumnDefinitions[1].Width =
            !vertical && showMirror && showGray ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        PreviewGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
        PreviewGrid.RowDefinitions[1].Height =
            vertical && showMirror && showGray ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        var first = _settings.MirrorFirst ? MirrorPanel : GrayPanel;
        var second = _settings.MirrorFirst ? GrayPanel : MirrorPanel;
        Grid.SetColumn(first, 0);
        Grid.SetRow(first, 0);
        Grid.SetColumn(second, vertical ? 0 : 1);
        Grid.SetRow(second, vertical ? 1 : 0);

        if (showMirror && showGray)
        {
            first.Margin = vertical ? new Thickness(0, 0, 0, 4) : new Thickness(0, 0, 4, 0);
            second.Margin = vertical ? new Thickness(0, 4, 0, 0) : new Thickness(4, 0, 0, 0);
        }
        else
        {
            var visiblePanel = showMirror ? MirrorPanel : GrayPanel;
            Grid.SetColumn(visiblePanel, 0);
            Grid.SetRow(visiblePanel, 0);
            visiblePanel.Margin = new Thickness(0);
        }
    }

    private void PreviewVisibility_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
            return;

        var showMirror = ShowMirrorCheckBox.IsChecked == true;
        var showGray = ShowGrayCheckBox.IsChecked == true;
        if (!showMirror && !showGray)
        {
            if (ReferenceEquals(sender, ShowMirrorCheckBox))
                ShowMirrorCheckBox.IsChecked = true;
            else
                ShowGrayCheckBox.IsChecked = true;
            return;
        }

        _settings.ShowMirror = showMirror;
        _settings.ShowGray = showGray;
        ApplyPreviewLayout();
        SettingsStore.Save(_settings);
    }

    private void Swap_Click(object sender, RoutedEventArgs e)
    {
        _settings.MirrorFirst = !_settings.MirrorFirst;
        ApplyPreviewLayout();
        SaveIfLoaded();
    }

    private void ToggleSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings.SettingsCollapsed = !_settings.SettingsCollapsed;
        ApplySettingsCollapsed();
        SaveIfLoaded();
    }

    private void ApplySettingsCollapsed()
    {
        SettingsControls.Visibility =
            _settings.SettingsCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ToggleSettingsButton.Content =
            _settings.SettingsCollapsed ? "設定を開く" : "設定をしまう";
        SettingsBar.Padding =
            _settings.SettingsCollapsed ? new Thickness(4) : new Thickness(10);
        ToggleSettingsButton.Margin =
            _settings.SettingsCollapsed ? new Thickness(0) : new Thickness(10, 0, 0, 0);
    }

    private void Preview_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Image image)
            return;

        var state = GetPreviewViewState(image);
        var oldScale = state.Scale.ScaleX;
        var factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        var newScale = Math.Clamp(oldScale * factor, 0.25, 8.0);
        var parent = image.Parent as IInputElement ?? image;
        var cursor = e.GetPosition(parent);
        var centerX = image.ActualWidth / 2.0;
        var centerY = image.ActualHeight / 2.0;
        var ratio = newScale / oldScale;

        state.Translate.X = (cursor.X - centerX) * (1 - ratio) + state.Translate.X * ratio;
        state.Translate.Y = (cursor.Y - centerY) * (1 - ratio) + state.Translate.Y * ratio;
        state.Scale.ScaleX = newScale;
        state.Scale.ScaleY = newScale;
        ClampPreviewTranslation(image, state);
        e.Handled = true;
    }

    private void Preview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image image)
            return;

        var state = GetPreviewViewState(image);
        if (e.ClickCount >= 2)
        {
            ResetPreviewView(state);
            e.Handled = true;
            return;
        }

        state.IsDragging = true;
        state.LastDragPoint = e.GetPosition(this);
        image.CaptureMouse();
        image.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void Preview_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Image image)
            return;

        var state = GetPreviewViewState(image);
        if (!state.IsDragging || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = e.GetPosition(this);
        state.Translate.X += current.X - state.LastDragPoint.X;
        state.Translate.Y += current.Y - state.LastDragPoint.Y;
        state.LastDragPoint = current;
        ClampPreviewTranslation(image, state);
        e.Handled = true;
    }

    private void Preview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image image)
            return;

        var state = GetPreviewViewState(image);
        state.IsDragging = false;
        image.ReleaseMouseCapture();
        image.Cursor = Cursors.Hand;
        e.Handled = true;
    }

    private PreviewViewState GetPreviewViewState(Image image)
    {
        if (_previewViewStates.TryGetValue(image, out var existing))
            return existing;

        var state = new PreviewViewState();
        var transforms = new TransformGroup();
        transforms.Children.Add(state.Scale);
        transforms.Children.Add(state.Translate);
        image.RenderTransform = transforms;
        _previewViewStates.Add(image, state);
        return state;
    }

    private static void ClampPreviewTranslation(Image image, PreviewViewState state)
    {
        if (state.Scale.ScaleX <= 1)
        {
            state.Translate.X = 0;
            state.Translate.Y = 0;
            return;
        }

        var maximumX = image.ActualWidth * (state.Scale.ScaleX - 1) / 2;
        var maximumY = image.ActualHeight * (state.Scale.ScaleY - 1) / 2;
        state.Translate.X = Math.Clamp(state.Translate.X, -maximumX, maximumX);
        state.Translate.Y = Math.Clamp(state.Translate.Y, -maximumY, maximumY);
    }

    private static void ResetPreviewView(PreviewViewState state)
    {
        state.Scale.ScaleX = 1;
        state.Scale.ScaleY = 1;
        state.Translate.X = 0;
        state.Translate.Y = 0;
    }

    private void AnalysisMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (AnalysisModeComboBox.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string modeName ||
            !Enum.TryParse<AnalysisMode>(modeName, out _))
            return;

        _settings.AnalysisMode = modeName;
        UpdateAnalysisLabel();
        SaveIfLoaded();
    }

    private void UpdateAnalysisLabel()
    {
        var mode = ParseAnalysisMode(_settings.AnalysisMode);
        AnalysisLabel.Text = mode switch
        {
            AnalysisMode.PerceptualLuminance => "知覚輝度",
            AnalysisMode.HslDesaturation => "彩度0（HSL）",
            AnalysisMode.ThreeTone => "3階調",
            AnalysisMode.FiveTone => "5階調",
            AnalysisMode.BlurredLuminance => "ぼかし＋知覚輝度",
            AnalysisMode.OriginalColor => "カラー（通常）",
            _ => "知覚輝度"
        };
    }

    private void Fps_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (FpsComboBox.SelectedItem is ComboBoxItem item &&
            double.TryParse(item.Tag?.ToString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var fps))
        {
            _settings.Fps = fps;
            ApplyTimerInterval();
            SaveIfLoaded();
        }
    }

    private void Topmost_Changed(object sender, RoutedEventArgs e)
    {
        Topmost = TopmostCheckBox.IsChecked == true;
        _settings.Topmost = Topmost;
        SaveIfLoaded();
    }

    private void ApplyTimerInterval()
    {
        _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Clamp(_settings.Fps, 0.5, 60));
    }

    private async void Capture_Tick(object? sender, EventArgs e)
    {
        if (_captureBusy || !_settings.CaptureRegion.IsValid)
            return;

        _captureBusy = true;
        try
        {
            var region = _settings.CaptureRegion;
            var showMirror = _settings.ShowMirror;
            var showGray = _settings.ShowGray;
            var analysisMode = ParseAnalysisMode(_settings.AnalysisMode);
            var frames = await Task.Run(
                () => CreatePreviewFrames(region, showMirror, showGray, analysisMode));
            if (frames.Mirror is not null)
                MirrorImage.Source = frames.Mirror;
            if (frames.Gray is not null)
                GrayImage.Source = frames.Gray;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"取得できません: {ex.Message}";
        }
        finally
        {
            _captureBusy = false;
        }
    }

    private static PreviewFrames CreatePreviewFrames(
        CaptureRegion region, bool showMirror, bool showGray, AnalysisMode analysisMode)
    {
        var source = CaptureRegion(region);
        BitmapSource? mirror = null;
        BitmapSource? gray = null;

        if (showMirror)
        {
            mirror = new TransformedBitmap(source, new ScaleTransform(-1, 1));
            mirror.Freeze();
        }

        if (showGray)
            gray = CreateAnalysisPreview(source, analysisMode);

        return new PreviewFrames(mirror, gray);
    }

    private static BitmapSource CreateAnalysisPreview(BitmapSource source, AnalysisMode mode)
    {
        if (mode == AnalysisMode.OriginalColor)
            return source;

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        converted.CopyPixels(pixels, stride, 0);

        var luminance = new byte[width * height];
        for (var pixelIndex = 0; pixelIndex < luminance.Length; pixelIndex++)
        {
            var byteIndex = pixelIndex * 4;
            var blue = pixels[byteIndex];
            var green = pixels[byteIndex + 1];
            var red = pixels[byteIndex + 2];

            luminance[pixelIndex] = mode == AnalysisMode.HslDesaturation
                ? HslLightness(red, green, blue)
                : PerceptualLuminance(red, green, blue);
        }

        switch (mode)
        {
            case AnalysisMode.ThreeTone:
                Quantize(luminance, 3);
                break;
            case AnalysisMode.FiveTone:
                Quantize(luminance, 5);
                break;
            case AnalysisMode.BlurredLuminance:
                var radius = Math.Clamp(Math.Min(width, height) / 80, 2, 18);
                luminance = BoxBlur(luminance, width, height, radius);
                break;
        }

        for (var pixelIndex = 0; pixelIndex < luminance.Length; pixelIndex++)
        {
            var byteIndex = pixelIndex * 4;
            var value = luminance[pixelIndex];
            pixels[byteIndex] = value;
            pixels[byteIndex + 1] = value;
            pixels[byteIndex + 2] = value;
            pixels[byteIndex + 3] = 255;
        }

        var result = BitmapSource.Create(
            width, height, source.DpiX, source.DpiY,
            PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    private static byte PerceptualLuminance(byte red, byte green, byte blue) =>
        (byte)((54 * red + 183 * green + 19 * blue + 128) >> 8);

    private static byte HslLightness(byte red, byte green, byte blue)
    {
        var maximum = Math.Max(red, Math.Max(green, blue));
        var minimum = Math.Min(red, Math.Min(green, blue));
        return (byte)((maximum + minimum) / 2);
    }

    private static void Quantize(byte[] values, int levels)
    {
        var maximumLevel = levels - 1;
        for (var index = 0; index < values.Length; index++)
        {
            var level = (values[index] * maximumLevel + 127) / 255;
            values[index] = (byte)((level * 255 + maximumLevel / 2) / maximumLevel);
        }
    }

    private static byte[] BoxBlur(byte[] source, int width, int height, int radius)
    {
        var horizontal = new byte[source.Length];
        var result = new byte[source.Length];
        var diameter = radius * 2 + 1;

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * width;
            var sum = 0;
            for (var offset = -radius; offset <= radius; offset++)
                sum += source[rowStart + Math.Clamp(offset, 0, width - 1)];

            for (var x = 0; x < width; x++)
            {
                horizontal[rowStart + x] = (byte)(sum / diameter);
                var removeX = Math.Clamp(x - radius, 0, width - 1);
                var addX = Math.Clamp(x + radius + 1, 0, width - 1);
                sum += source[rowStart + addX] - source[rowStart + removeX];
            }
        }

        for (var x = 0; x < width; x++)
        {
            var sum = 0;
            for (var offset = -radius; offset <= radius; offset++)
                sum += horizontal[Math.Clamp(offset, 0, height - 1) * width + x];

            for (var y = 0; y < height; y++)
            {
                result[y * width + x] = (byte)(sum / diameter);
                var removeY = Math.Clamp(y - radius, 0, height - 1);
                var addY = Math.Clamp(y + radius + 1, 0, height - 1);
                sum += horizontal[addY * width + x] - horizontal[removeY * width + x];
            }
        }

        return result;
    }

    private static BitmapSource CaptureRegion(CaptureRegion region)
    {
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            throw new InvalidOperationException("画面のデバイスコンテキストを取得できません。");

        var memoryDc = CreateCompatibleDC(screenDc);
        if (memoryDc == IntPtr.Zero)
        {
            ReleaseDC(IntPtr.Zero, screenDc);
            throw new InvalidOperationException("画像用のデバイスコンテキストを作成できません。");
        }

        var bitmap = CreateCompatibleBitmap(screenDc, region.Width, region.Height);
        if (bitmap == IntPtr.Zero)
        {
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
            throw new InvalidOperationException("取得画像を作成できません。");
        }

        var oldBitmap = SelectObject(memoryDc, bitmap);
        try
        {
            const int SourceCopy = 0x00CC0020;
            const int CaptureLayeredWindows = 0x40000000;
            if (!BitBlt(memoryDc, 0, 0, region.Width, region.Height,
                    screenDc, region.X, region.Y, SourceCopy | CaptureLayeredWindows))
            {
                throw new InvalidOperationException("指定範囲の画面取得に失敗しました。");
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            SelectObject(memoryDc, oldBitmap);
            DeleteObject(bitmap);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void RestoreWindowBounds()
    {
        if (_settings.WindowWidth >= MinWidth)
            Width = _settings.WindowWidth;
        if (_settings.WindowHeight >= MinHeight)
            Height = _settings.WindowHeight;

        if (IsVisibleOnAnyScreen(_settings.WindowLeft, _settings.WindowTop))
        {
            Left = _settings.WindowLeft;
            Top = _settings.WindowTop;
        }
    }

    private static bool IsVisibleOnAnyScreen(double left, double top) =>
        left >= SystemParameters.VirtualScreenLeft - 100 &&
        left <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
        top >= SystemParameters.VirtualScreenTop - 100 &&
        top <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }
        SettingsStore.Save(_settings);
    }

    private string RegionStatus() =>
        $"取得範囲: {_settings.CaptureRegion.Width} × {_settings.CaptureRegion.Height}  " +
        $"({_settings.CaptureRegion.X}, {_settings.CaptureRegion.Y})";

    private void SaveIfLoaded()
    {
        if (_loaded)
            SettingsStore.Save(_settings);
    }

    private static int FpsToIndex(double fps) => fps switch
    {
        <= 0.5 => 0,
        <= 1 => 1,
        <= 2 => 2,
        <= 5 => 3,
        <= 10 => 4,
        <= 15 => 5,
        _ => 6
    };

    private static AnalysisMode ParseAnalysisMode(string? value) =>
        Enum.TryParse<AnalysisMode>(value, out var mode)
            ? mode
            : AnalysisMode.PerceptualLuminance;

    private static int AnalysisModeToIndex(string? value) => ParseAnalysisMode(value) switch
    {
        AnalysisMode.PerceptualLuminance => 0,
        AnalysisMode.HslDesaturation => 1,
        AnalysisMode.ThreeTone => 2,
        AnalysisMode.FiveTone => 3,
        AnalysisMode.BlurredLuminance => 4,
        AnalysisMode.OriginalColor => 5,
        _ => 0
    };

    private enum AnalysisMode
    {
        PerceptualLuminance,
        HslDesaturation,
        ThreeTone,
        FiveTone,
        BlurredLuminance,
        OriginalColor
    }

    private sealed record PreviewFrames(BitmapSource? Mirror, BitmapSource? Gray);

    private sealed class PreviewViewState
    {
        public ScaleTransform Scale { get; } = new(1, 1);
        public TranslateTransform Translate { get; } = new();
        public bool IsDragging { get; set; }
        public System.Windows.Point LastDragPoint { get; set; }
    }

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr deviceContext);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr deviceContext, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr deviceContext, IntPtr graphicsObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        IntPtr destination, int destinationX, int destinationY, int width, int height,
        IntPtr source, int sourceX, int sourceY, int rasterOperation);
}

