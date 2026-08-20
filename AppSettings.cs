using System.Text.Json;
using System.IO;

namespace ClipCanvasMirror;

public sealed class AppSettings
{
    public CaptureRegion CaptureRegion { get; set; } = new();
    public bool VerticalLayout { get; set; }
    public bool ShowMirror { get; set; } = true;
    public bool ShowGray { get; set; } = true;
    public bool MirrorFirst { get; set; } = true;
    public string AnalysisMode { get; set; } = "PerceptualLuminance";
    public bool SettingsCollapsed { get; set; }
    public bool Topmost { get; set; } = true;
    public double Fps { get; set; } = 15;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 1040;
    public double WindowHeight { get; set; } = 620;
}

public sealed record CaptureRegion(int X = 0, int Y = 0, int Width = 0, int Height = 0)
{
    public bool IsValid => Width > 0 && Height > 0;
}

public static class SettingsStore
{
    private static readonly string FolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClipCanvasMirror");
    private static readonly string FilePath = Path.Combine(FolderPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath))
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // 設定保存の失敗は、プレビュー表示自体を止めない。
        }
    }
}

