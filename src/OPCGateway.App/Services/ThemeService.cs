using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OPCGateway.Core.Configuration;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;

namespace OPCGateway.App.Services;

public enum ThemeMode
{
    /// <summary>跟隨 Windows 的深淺色設定。</summary>
    System,
    Light,
    Dark,
}

/// <summary>
/// 主題管理：跟隨系統或手動指定深淺色。
/// 淺色為「Fluent 藍」（WinUI 語意色、主色 #0067C0），深色為「石墨青」（低亮度表面、主色 #2DD4BF）。
/// 切換順序固定為：先把該主題的表面色、狀態色與主色寫進 Application.Resources（直接項目優先於合併字典），
/// 再請 WPF-UI 換掉主題字典。WPF-UI 主題字典裡的筆刷（例如 AccentButtonBackground）是以 DynamicResource
/// 參考 AccentFillColorDefault 等顏色，且只在字典載入後第一次使用時解析，所以主色一定要在換字典之前設定好。
/// </summary>
public sealed class ThemeService
{
    private const string ResourceFolder = "pack://application:,,,/Resources/";
    private const string WpfUiThemeFolder = "Wpf.Ui;component/Resources/Theme/";

    private static readonly Color LightAccent = (Color)ColorConverter.ConvertFromString("#0067C0");
    private static readonly Color DarkAccent = (Color)ColorConverter.ConvertFromString("#2DD4BF");

    private readonly ILogger<ThemeService> _logger;
    private readonly List<object> _appliedKeys = new();
    private Window? _window;
    private bool _watching;
    private bool _applying;

    public ThemeService(ILogger<ThemeService>? logger = null)
    {
        _logger = logger ?? NullLogger<ThemeService>.Instance;
    }

    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    /// <summary>目前實際生效的主題（跟隨系統時為系統當下的值）。</summary>
    public ApplicationTheme Current { get; private set; } = ApplicationTheme.Light;

    public bool IsDark => Current == ApplicationTheme.Dark;

    public event EventHandler<ApplicationTheme>? Changed;

    public void Attach(Window window)
    {
        if (_window != null)
            return;
        _window = window;
        ApplicationThemeManager.Changed += OnFrameworkThemeChanged;
        LogDiagnostics();
        Apply(Mode);
    }

    public void Apply(ThemeMode mode)
    {
        Mode = mode;
        if (_window == null)
        {
            _logger.LogDebug("主題模式設為 {Mode}，等待主視窗載入後套用", mode);
            return;
        }

        if (mode == ThemeMode.System)
        {
            // 只註冊系統主題變更的監看；WPF-UI 的 Watch 在視窗已載入時不會主動套用主題，所以下面自己算
            if (!_watching)
            {
                SystemThemeWatcher.Watch(_window, WindowBackdropType.None, false);
                _watching = true;
            }
        }
        else if (_watching)
        {
            SystemThemeWatcher.UnWatch(_window);
            _watching = false;
        }

        var target = mode switch
        {
            ThemeMode.Light => ApplicationTheme.Light,
            ThemeMode.Dark => ApplicationTheme.Dark,
            _ => GetSystemApplicationTheme(),
        };
        _logger.LogDebug("套用主題模式 {Mode} → {Theme}", mode, target);
        Finish(target);
    }

    /// <summary>在淺色與深色之間切換（離開跟隨系統模式）。</summary>
    public void Toggle() => Apply(IsDark ? ThemeMode.Light : ThemeMode.Dark);

    public static ThemeMode Parse(string? text) => ConfigStore.NormalizeTheme(text) switch
    {
        "Light" => ThemeMode.Light,
        "Dark" => ThemeMode.Dark,
        _ => ThemeMode.System,
    };

    private static ApplicationTheme GetSystemApplicationTheme()
    {
        SystemThemeManager.UpdateSystemThemeCache();
        return ApplicationThemeManager.GetSystemTheme() switch
        {
            SystemTheme.Dark or SystemTheme.CapturedMotion or SystemTheme.Glow => ApplicationTheme.Dark,
            SystemTheme.HC1 or SystemTheme.HC2 or SystemTheme.HCBlack or SystemTheme.HCWhite => ApplicationTheme.HighContrast,
            _ => ApplicationTheme.Light,
        };
    }

    /// <summary>跟隨系統時，Windows 主題改變會由 WPF-UI 的監看器換字典並觸發此事件；這裡再依新主題補上主色與語意色。</summary>
    private void OnFrameworkThemeChanged(ApplicationTheme currentApplicationTheme, Color systemAccent)
    {
        if (_applying || Mode != ThemeMode.System)
            return;
        _logger.LogDebug("系統主題變更：{Theme}", currentApplicationTheme);
        Finish(currentApplicationTheme);
    }

    private void Finish(ApplicationTheme theme)
    {
        // 高對比沿用淺色的語意色；WPF-UI 本身會載入高對比字典
        var effective = theme == ApplicationTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
        var frameworkTheme = theme == ApplicationTheme.HighContrast ? ApplicationTheme.HighContrast : effective;

        _applying = true;
        try
        {
            ApplyResourceOverrides(effective);
            ApplyAccent(effective);
            ApplicationThemeManager.Apply(frameworkTheme, WindowBackdropType.None, false);

            var reported = ApplicationThemeManager.GetAppTheme();
            if (reported != frameworkTheme)
            {
                _logger.LogWarning("WPF-UI 未換成 {Theme} 主題字典（回報 {Reported}），改由程式直接替換", frameworkTheme, reported);
                ReplaceThemeDictionary(frameworkTheme);
            }
        }
        finally
        {
            _applying = false;
        }

        if (_window != null)
        {
            if (effective == ApplicationTheme.Dark)
                WindowBackgroundManager.ApplyDarkThemeToWindow(_window);
            else
                WindowBackgroundManager.RemoveDarkThemeFromWindow(_window);
        }

        var changed = effective != Current;
        Current = effective;
        if (changed)
            _logger.LogInformation("介面主題：{Theme}（模式 {Mode}）", effective == ApplicationTheme.Dark ? "深色" : "淺色", Mode);
        Changed?.Invoke(this, effective);
    }

    private void ApplyResourceOverrides(ApplicationTheme theme)
    {
        var app = Application.Current;
        if (app == null)
            return;

        foreach (var key in _appliedKeys)
            app.Resources.Remove(key);
        _appliedKeys.Clear();

        var suffix = theme == ApplicationTheme.Dark ? "Dark" : "Light";
        foreach (var file in new[] { $"Surfaces.{suffix}.xaml", $"SemanticColors.{suffix}.xaml" })
        {
            var semantic = file.StartsWith("SemanticColors", StringComparison.Ordinal);
            var dictionary = new ResourceDictionary { Source = new Uri(ResourceFolder + file, UriKind.Absolute) };
            foreach (var key in dictionary.Keys)
            {
                var value = dictionary[key];
                // 語意色同時維護一份給轉換器用的「活」筆刷（同一實例、只改顏色）；
                // Application.Resources 裡放的是凍結副本，給 XAML 的 DynamicResource 用。
                if (semantic && value is SolidColorBrush brush)
                    SemanticBrushes.Set(key, brush.Color, brush.Opacity);

                if (value is Freezable freezable && freezable.CanFreeze)
                    freezable.Freeze();
                app.Resources[key] = value;
                _appliedKeys.Add(key);
            }
        }
    }

    private static void ApplyAccent(ApplicationTheme theme)
    {
        // 直接指定主色與三個層次（WPF-UI 會據此更新 SystemAccentColor* 與 AccentFillColor* 資源）
        if (theme == ApplicationTheme.Dark)
        {
            ApplicationAccentColorManager.Apply(
                DarkAccent,
                DarkAccent,
                (Color)ColorConverter.ConvertFromString("#41DCC9"),
                (Color)ColorConverter.ConvertFromString("#5EEAD4"));
        }
        else
        {
            ApplicationAccentColorManager.Apply(
                LightAccent,
                LightAccent,
                (Color)ColorConverter.ConvertFromString("#005EB0"),
                (Color)ColorConverter.ConvertFromString("#00549E"));
        }
    }

    /// <summary>後備方案：直接在 Application.Resources 的合併字典裡替換 WPF-UI 的主題字典。</summary>
    private void ReplaceThemeDictionary(ApplicationTheme theme)
    {
        var app = Application.Current;
        if (app == null)
            return;

        var merged = app.Resources.MergedDictionaries;
        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.ToString();
            if (source != null && source.IndexOf(WpfUiThemeFolder, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                merged[i] = new ThemesDictionary { Theme = theme };
                return;
            }
        }
        _logger.LogError("找不到 WPF-UI 的主題字典，無法切換主題");
    }

    private void LogDiagnostics()
    {
        if (!_logger.IsEnabled(LogLevel.Debug))
            return;
        var app = Application.Current;
        var sources = app == null
            ? "(無 Application)"
            : string.Join(" | ", app.Resources.MergedDictionaries.Select(d => d.Source?.ToString() ?? "(inline)"));
        _logger.LogDebug("WPF-UI IsApplication={IsApplication}；Application 合併字典：{Sources}", UiApplication.Current.IsApplication, sources);
    }
}
