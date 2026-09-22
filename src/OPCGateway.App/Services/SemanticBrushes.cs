using System.Windows.Media;

namespace OPCGateway.App.Services;

/// <summary>
/// 給轉換器用的語意色筆刷登錄表。每個鍵在整個程式生命週期都是同一個未凍結的 SolidColorBrush，
/// 切換主題時由 <see cref="ThemeService"/> 只改 Color，已綁定這些筆刷的元素（日誌列、狀態膠囊）會跟著換色。
/// 不能直接把它放進 ResourceDictionary：WPF 會在加入時把可凍結物件凍結，之後就改不了顏色；
/// XAML 端的 DynamicResource 仍然用 Application.Resources 裡的凍結副本。
/// </summary>
internal static class SemanticBrushes
{
    private static readonly Dictionary<object, SolidColorBrush> Live = new();

    public static Brush? Get(object key) => Live.TryGetValue(key, out var brush) ? brush : null;

    public static void Set(object key, Color color, double opacity)
    {
        if (Live.TryGetValue(key, out var live))
        {
            live.Color = color;
            live.Opacity = opacity;
            return;
        }
        Live[key] = new SolidColorBrush(color) { Opacity = opacity };
    }
}
