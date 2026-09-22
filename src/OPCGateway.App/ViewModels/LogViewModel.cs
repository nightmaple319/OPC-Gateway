using System.IO;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OPCGateway.Core.Logging;

namespace OPCGateway.App.ViewModels;

/// <summary>日誌頁：等級與關鍵字篩選、自動捲動、匯出。</summary>
public sealed partial class LogViewModel : ObservableObject
{
    private readonly LogBuffer _buffer;
    private readonly ConcurrentQueue<LogEntry> _pending = new();
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private LogSeverity _minSeverity = LogSeverity.Info;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private bool _autoScroll = true;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private long _errorCount;
    [ObservableProperty] private long _warningCount;
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private LogEntry? _selectedEntry;

    public LogViewModel(LogBuffer buffer)
    {
        _buffer = buffer;
        foreach (var entry in _buffer.Snapshot())
            Entries.Add(entry);

        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = FilterEntry;

        _buffer.EntryAdded += (_, entry) => _pending.Enqueue(entry);
        _buffer.Cleared += (_, _) => Application.Current.Dispatcher.InvokeAsync(() =>
        {
            while (_pending.TryDequeue(out _)) { }
            Entries.Clear();
            TotalCount = 0;
        });

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += (_, _) => Drain();
        _timer.Start();
        TotalCount = Entries.Count;
    }

    public ObservableCollection<LogEntry> Entries { get; } = new();
    public ICollectionView EntriesView { get; }
    public IReadOnlyList<LogSeverity> SeverityOptions { get; } = new[]
    {
        LogSeverity.Trace, LogSeverity.Debug, LogSeverity.Info, LogSeverity.Warn, LogSeverity.Error,
    };

    /// <summary>有新資料且開啟自動捲動時觸發，由頁面捲到最後一筆。</summary>
    public event Action? ScrollToEndRequested;

    partial void OnMinSeverityChanged(LogSeverity value) => EntriesView.Refresh();
    partial void OnFilterTextChanged(string value) => EntriesView.Refresh();

    private bool FilterEntry(object item)
    {
        if (item is not LogEntry entry)
            return false;
        if (entry.Severity < MinSeverity)
            return false;
        if (string.IsNullOrWhiteSpace(FilterText))
            return true;
        return entry.Message.IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) >= 0
               || entry.Logger.IndexOf(FilterText, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void Drain()
    {
        ErrorCount = _buffer.ErrorCount;
        WarningCount = _buffer.WarningCount;

        if (IsPaused || _pending.IsEmpty)
            return;

        var added = false;
        while (_pending.TryDequeue(out var entry))
        {
            Entries.Add(entry);
            added = true;
        }

        while (Entries.Count > _buffer.Capacity)
            Entries.RemoveAt(0);

        TotalCount = Entries.Count;
        if (added && AutoScroll)
            ScrollToEndRequested?.Invoke();
    }

    [RelayCommand]
    private void Clear()
    {
        _buffer.Clear();
    }

    [RelayCommand]
    private void TogglePause() => IsPaused = !IsPaused;

    [RelayCommand]
    private void Export()
    {
        var dialog = new SaveFileDialog
        {
            Title = "匯出日誌",
            Filter = "文字檔 (*.txt)|*.txt|所有檔案 (*.*)|*.*",
            FileName = $"gateway_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
        };
        if (dialog.ShowDialog() != true)
            return;

        var lines = _buffer.Snapshot().Select(e => e.ToString());
        File.WriteAllLines(dialog.FileName, lines);
    }

    [RelayCommand]
    private void CopyEntry(LogEntry? entry)
    {
        entry ??= SelectedEntry;
        if (entry == null)
            return;
        try { Clipboard.SetText(entry.ToString()); }
        catch { /* ignore */ }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "Logs");
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }
}
