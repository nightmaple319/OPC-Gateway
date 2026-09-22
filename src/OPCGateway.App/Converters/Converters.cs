using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using OPCGateway.Core.Da;
using OPCGateway.Core.Engine;
using OPCGateway.App.Services;
using OPCGateway.Core.Logging;
using Wpf.Ui.Controls;

namespace OPCGateway.App.Converters;

internal static class BrushLookup
{
    /// <summary>優先用 ThemeService 維護的活筆刷（換主題只改 Color，已綁定的元素會跟著變），找不到才查資源。</summary>
    public static Brush? Find(string key) => SemanticBrushes.Get(key) ?? Application.Current?.TryFindResource(key) as Brush;

    public static Brush? Good() => Find("StatusGoodBrush");
    public static Brush? GoodBackground() => Find("StatusGoodBackgroundBrush");
    public static Brush? Warning() => Find("StatusWarningBrush");
    public static Brush? WarningBackground() => Find("StatusWarningBackgroundBrush");
    public static Brush? Bad() => Find("StatusBadBrush");
    public static Brush? BadBackground() => Find("StatusBadBackgroundBrush");
    public static Brush? Info() => Find("StatusInfoBrush");
    public static Brush? InfoBackground() => Find("StatusInfoBackgroundBrush");
    public static Brush? Neutral() => Find("StatusNeutralBrush");
    public static Brush? NeutralBackground() => Find("StatusNeutralBackgroundBrush");
    /// <summary>正常狀態的文字色。用語意字典裡的 StatusNormalBrush（同一實例、換主題只改 Color），
    /// 不能用 WPF-UI 的 TextFillColorPrimaryBrush：那個筆刷會隨主題字典整個換掉，已綁定的列會留在舊色。</summary>
    public static Brush? Text() => Find("StatusNormalBrush");
}

/// <summary>品質主狀態 → 前景色。Good 不上色（ISA-101：正常不上色）。</summary>
public sealed class QualityToForegroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DaQualityMaster.Good => BrushLookup.Text(),
        DaQualityMaster.Uncertain => BrushLookup.Warning(),
        _ => BrushLookup.Bad(),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class QualityToBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DaQualityMaster.Good => Brushes.Transparent,
        DaQualityMaster.Uncertain => BrushLookup.WarningBackground(),
        _ => BrushLookup.BadBackground(),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class TagStateToForegroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TagState.Active => BrushLookup.Good(),
        TagState.WaitingForData => BrushLookup.Info(),
        TagState.Error => BrushLookup.Bad(),
        _ => BrushLookup.Neutral(),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class TagStateToBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TagState.Active => BrushLookup.GoodBackground(),
        TagState.WaitingForData => BrushLookup.InfoBackground(),
        TagState.Error => BrushLookup.BadBackground(),
        _ => BrushLookup.NeutralBackground(),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class TagStateToSymbolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TagState.Active => SymbolRegular.CheckmarkCircle24,
        TagState.WaitingForData => SymbolRegular.Clock24,
        TagState.Error => SymbolRegular.ErrorCircle24,
        TagState.Disabled => SymbolRegular.Pause24,
        _ => SymbolRegular.PlugDisconnected24,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>布林 → 連線膠囊色（連線＝綠、未連線＝紅，這是 IT 工具慣例，配合圖示與文字一起使用）。</summary>
public sealed class BoolToStatusBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var background = string.Equals(parameter as string, "background", StringComparison.OrdinalIgnoreCase);
        return value is true
            ? (background ? BrushLookup.GoodBackground() : BrushLookup.Good())
            : (background ? BrushLookup.BadBackground() : BrushLookup.Bad());
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class RunStateToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var background = string.Equals(parameter as string, "background", StringComparison.OrdinalIgnoreCase);
        return value switch
        {
            GatewayRunState.Running => background ? BrushLookup.GoodBackground() : BrushLookup.Good(),
            GatewayRunState.Degraded => background ? BrushLookup.WarningBackground() : BrushLookup.Warning(),
            GatewayRunState.Starting or GatewayRunState.Stopping => background ? BrushLookup.InfoBackground() : BrushLookup.Info(),
            _ => background ? BrushLookup.NeutralBackground() : BrushLookup.Neutral(),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class LogSeverityToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var background = string.Equals(parameter as string, "background", StringComparison.OrdinalIgnoreCase);
        return value switch
        {
            LogSeverity.Error or LogSeverity.Fatal => background ? BrushLookup.BadBackground() : BrushLookup.Bad(),
            LogSeverity.Warn => background ? BrushLookup.WarningBackground() : BrushLookup.Warning(),
            LogSeverity.Info => background ? Brushes.Transparent : BrushLookup.Text(),
            _ => background ? Brushes.Transparent : BrushLookup.Neutral(),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var invert = string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase);
        var isNull = value == null || (value is string s && string.IsNullOrEmpty(s));
        return (isNull ^ invert) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>整數 0 → Visible（給空狀態用），參數 "nonzero" 反過來。</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var count = value switch { int i => i, long l => (int)Math.Min(l, int.MaxValue), _ => 0 };
        var nonZero = string.Equals(parameter as string, "nonzero", StringComparison.OrdinalIgnoreCase);
        return (count == 0) ^ nonZero ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class DateTimeToLocalTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var format = parameter as string ?? "yyyy-MM-dd HH:mm:ss.fff";
        return value switch
        {
            DateTime dt when dt == DateTime.MinValue => "—",
            DateTime dt => (dt.Kind == DateTimeKind.Utc ? dt.ToLocalTime() : dt).ToString(format, CultureInfo.InvariantCulture),
            _ => "—",
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class LogSeverityToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        LogSeverity.Trace => "追蹤",
        LogSeverity.Debug => "除錯",
        LogSeverity.Info => "資訊",
        LogSeverity.Warn => "警告",
        LogSeverity.Error => "錯誤",
        LogSeverity.Fatal => "嚴重",
        _ => value?.ToString() ?? string.Empty,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
