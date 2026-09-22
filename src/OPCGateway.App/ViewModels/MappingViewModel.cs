using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OPCGateway.Core.Configuration;
using OPCGateway.Core.Da;
using OPCGateway.Core.Engine;

namespace OPCGateway.App.ViewModels;

/// <summary>標籤頁：瀏覽 OPC DA 位址空間、搜尋、勾選加入映射。</summary>
public sealed partial class MappingViewModel : ObservableObject
{
    private readonly GatewayEngine _engine;
    private readonly ILogger<MappingViewModel> _logger;
    private readonly Dispatcher _dispatcher;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectDaCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCheckedCommand))]
    private bool _daConnected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectDaCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddCheckedCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _hasSearchResults;
    [ObservableProperty] private bool _hasTree;
    [ObservableProperty] private bool _searchTruncated;
    [ObservableProperty] private string _serverText = "—";
    [ObservableProperty] private string? _busyMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCheckedCommand))]
    private int _checkedCount;

    public MappingViewModel(GatewayEngine engine, ILogger<MappingViewModel> logger)
    {
        _engine = engine;
        _logger = logger;
        _dispatcher = Application.Current.Dispatcher;

        _engine.StateChanged += (_, _) => _dispatcher.InvokeAsync(OnEngineStateChanged);
        _engine.Tags.CollectionChanged += OnTagsChanged;
        OnEngineStateChanged();
    }

    public ObservableCollection<TreeNodeViewModel> RootNodes { get; } = new();
    public ObservableCollection<SearchResultViewModel> SearchResults { get; } = new();

    private int BrowseLimit => Math.Max(10, _engine.Config.Options.BrowseChildLimit);

    // ---------- 命令 ----------

    [RelayCommand(CanExecute = nameof(CanConnectDa))]
    private async Task ConnectDaAsync()
    {
        await RunBusyAsync("正在連線 OPC DA…", async () =>
        {
            await _engine.ConnectDaAsync();
            await LoadRootAsync();
        });
    }

    private bool CanConnectDa() => !IsBusy && !DaConnected;

    [RelayCommand(CanExecute = nameof(CanUseDa))]
    private Task RefreshAsync() => RunBusyAsync("正在讀取根目錄…", LoadRootAsync);

    private bool CanUseDa() => !IsBusy && DaConnected;

    [RelayCommand(CanExecute = nameof(CanUseDa))]
    private async Task SearchAsync()
    {
        var term = SearchText?.Trim();
        if (string.IsNullOrEmpty(term))
        {
            ClearSearch();
            return;
        }

        await RunBusyAsync($"正在搜尋「{term}」…", async () =>
        {
            const int max = 500;
            var results = await _engine.DaClient.SearchItemsAsync(term!, max);
            SearchResults.Clear();
            foreach (var node in results)
                SearchResults.Add(new SearchResultViewModel(node, _engine.FindTag(node.ItemId) != null, UpdateCheckedCount));
            HasSearchResults = SearchResults.Count > 0;
            SearchTruncated = results.Count >= max;
            StatusMessage = results.Count == 0
                ? $"找不到包含「{term}」的項目"
                : SearchTruncated ? $"找到超過 {max} 個項目，只顯示前 {max} 個，請縮小關鍵字" : $"找到 {results.Count} 個項目";
        });
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        SearchResults.Clear();
        HasSearchResults = false;
        SearchTruncated = false;
        UpdateCheckedCount();
    }

    [RelayCommand(CanExecute = nameof(CanAddChecked))]
    private async Task AddCheckedAsync()
    {
        var items = new Dictionary<string, TagMappingConfig>(StringComparer.Ordinal);
        foreach (var node in EnumerateChecked(RootNodes))
            items[node.ItemId] = new TagMappingConfig { ItemId = node.ItemId };
        foreach (var result in SearchResults.Where(r => r.IsChecked))
            items[result.Node.ItemId] = new TagMappingConfig { ItemId = result.Node.ItemId };

        if (items.Count == 0)
            return;

        await RunBusyAsync($"正在加入 {items.Count} 個標籤…", async () =>
        {
            var added = await _engine.AddTagsAsync(items.Values);
            foreach (var node in EnumerateChecked(RootNodes).ToList())
            {
                node.IsChecked = false;
                node.IsMapped = true;
            }
            foreach (var result in SearchResults.Where(r => r.IsChecked).ToList())
            {
                result.IsChecked = false;
                result.IsMapped = true;
            }
            StatusMessage = added.Count == items.Count
                ? $"已加入 {added.Count} 個標籤"
                : $"已加入 {added.Count} 個標籤，{items.Count - added.Count} 個已存在";
        });
    }

    private bool CanAddChecked() => !IsBusy && CheckedCount > 0;

    [RelayCommand]
    private async Task AddFolderAsync(TreeNodeViewModel? folder)
    {
        if (folder == null || !folder.IsFolder || !DaConnected)
            return;

        await RunBusyAsync($"正在列出「{folder.Name}」下的所有項目…", async () =>
        {
            const int max = 5000;
            var items = await _engine.DaClient.ListItemsRecursiveAsync(folder.ItemId, max);
            if (items.Count == 0)
            {
                StatusMessage = $"資料夾「{folder.Name}」下沒有項目";
                return;
            }

            if (items.Count > 200)
            {
                var confirm = MessageBox.Show($"資料夾「{folder.Name}」下有 {items.Count} 個項目{(items.Count >= max ? "（已達上限）" : string.Empty)}，確定全部加入嗎？",
                    "加入整個資料夾", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (confirm != MessageBoxResult.Yes)
                    return;
            }

            var added = await _engine.AddTagsAsync(items.Select(i => new TagMappingConfig { ItemId = i.ItemId }));
            StatusMessage = $"已從「{folder.Name}」加入 {added.Count} 個標籤";
            MarkMapped(RootNodes);
        });
    }

    [RelayCommand]
    private async Task AddNodeAsync(TreeNodeViewModel? node)
    {
        if (node == null || !node.IsItem || node.IsMapped)
            return;
        await RunBusyAsync("正在加入標籤…", async () =>
        {
            var added = await _engine.AddTagsAsync(new[] { new TagMappingConfig { ItemId = node.ItemId } });
            node.IsMapped = true;
            node.IsChecked = false;
            StatusMessage = added.Count > 0 ? $"已加入 {node.ItemId}" : $"{node.ItemId} 已存在";
        });
    }

    [RelayCommand]
    private async Task AddSearchResultAsync(SearchResultViewModel? result)
    {
        if (result == null || result.IsMapped)
            return;
        await RunBusyAsync("正在加入標籤…", async () =>
        {
            var added = await _engine.AddTagsAsync(new[] { new TagMappingConfig { ItemId = result.Node.ItemId } });
            result.IsMapped = true;
            result.IsChecked = false;
            StatusMessage = added.Count > 0 ? $"已加入 {result.Node.ItemId}" : $"{result.Node.ItemId} 已存在";
        });
    }

    [RelayCommand]
    private void UncheckAll()
    {
        foreach (var node in EnumerateChecked(RootNodes).ToList())
            node.IsChecked = false;
        foreach (var result in SearchResults)
            result.IsChecked = false;
        UpdateCheckedCount();
    }

    // ---------- 樹狀載入 ----------

    private async Task LoadRootAsync()
    {
        RootNodes.Clear();
        HasTree = false;
        var result = await _engine.DaClient.BrowseAsync(null, BrowseLimit);
        foreach (var node in result.Nodes)
            RootNodes.Add(CreateNode(node));
        HasTree = RootNodes.Count > 0;
        StatusMessage = result.Truncated
            ? $"根目錄項目超過 {BrowseLimit} 個，僅顯示部分；可用搜尋找到其餘項目"
            : $"根目錄有 {RootNodes.Count} 個項目";
    }

    private TreeNodeViewModel CreateNode(DaBrowseNode node)
    {
        var vm = new TreeNodeViewModel(node, LoadChildrenAsync, UpdateCheckedCount)
        {
            IsMapped = node.IsItem && _engine.FindTag(node.ItemId) != null,
        };
        return vm;
    }

    private async Task LoadChildrenAsync(TreeNodeViewModel parent)
    {
        try
        {
            var result = await _engine.DaClient.BrowseAsync(parent.ItemId, BrowseLimit);
            parent.SetChildren(result.Nodes.Select(CreateNode), result.Truncated);
            if (result.Truncated)
                StatusMessage = $"「{parent.Name}」的子項目超過 {BrowseLimit} 個，僅顯示部分";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "載入 {ItemId} 的子節點失敗", parent.ItemId);
            parent.SetError(ex.Message);
        }
    }

    private static IEnumerable<TreeNodeViewModel> EnumerateChecked(IEnumerable<TreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsPlaceholder)
                continue;
            if (node.IsItem && node.IsChecked)
                yield return node;
            foreach (var child in EnumerateChecked(node.Children))
                yield return child;
        }
    }

    private void MarkMapped(IEnumerable<TreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsPlaceholder)
                continue;
            if (node.IsItem)
                node.IsMapped = _engine.FindTag(node.ItemId) != null;
            MarkMapped(node.Children);
        }
    }

    private void UpdateCheckedCount()
    {
        CheckedCount = EnumerateChecked(RootNodes).Count() + SearchResults.Count(r => r.IsChecked);
    }

    private void OnEngineStateChanged()
    {
        var connected = _engine.DaConnected;
        var wasConnected = DaConnected;
        DaConnected = connected;
        ServerText = connected ? $"{_engine.DaClient.ProgId} @ {_engine.DaClient.Host}" : $"{_engine.Config.DaSource.ProgId} @ {_engine.Config.DaSource.Host}（未連線）";

        if (!connected)
        {
            RootNodes.Clear();
            HasTree = false;
            SearchResults.Clear();
            HasSearchResults = false;
            UpdateCheckedCount();
        }
        else if (!wasConnected && RootNodes.Count == 0 && !IsBusy)
        {
            _ = RunBusyAsync("正在讀取根目錄…", LoadRootAsync);
        }
    }

    private void OnTagsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _dispatcher.InvokeAsync(() =>
        {
            MarkMapped(RootNodes);
            foreach (var result in SearchResults)
                result.IsMapped = _engine.FindTag(result.Node.ItemId) != null;
        }, DispatcherPriority.Background);
    }

    private async Task RunBusyAsync(string message, Func<Task> action)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        BusyMessage = message;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "操作失敗: {Message}", message);
            StatusMessage = "失敗：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
        }
    }
}

/// <summary>樹狀節點：資料夾以佔位子節點顯示展開箭頭，展開時延遲載入。</summary>
public sealed partial class TreeNodeViewModel : ObservableObject
{
    private readonly Func<TreeNodeViewModel, Task>? _loader;
    private readonly Action? _checkedChanged;

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _isMapped;
    [ObservableProperty] private bool _isTruncated;
    [ObservableProperty] private string? _error;

    public TreeNodeViewModel(DaBrowseNode node, Func<TreeNodeViewModel, Task> loader, Action checkedChanged)
    {
        Node = node;
        _loader = loader;
        _checkedChanged = checkedChanged;
        if (node.HasChildren)
            Children.Add(CreatePlaceholder());
    }

    private TreeNodeViewModel(string placeholderText)
    {
        Node = new DaBrowseNode(placeholderText, string.Empty, false, false);
        IsPlaceholder = true;
    }

    public static TreeNodeViewModel CreatePlaceholder() => new("載入中…");

    public DaBrowseNode Node { get; }
    public bool IsPlaceholder { get; }
    public string Name => Node.Name;
    public string ItemId => Node.ItemId;
    public bool IsFolder => Node.HasChildren;
    public bool IsItem => Node.IsItem && !Node.HasChildren;
    public ObservableCollection<TreeNodeViewModel> Children { get; } = new();

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && IsFolder && !IsLoaded && !IsLoading && _loader != null)
            _ = LoadAsync();
    }

    partial void OnIsCheckedChanged(bool value) => _checkedChanged?.Invoke();

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            await _loader!(this);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void SetChildren(IEnumerable<TreeNodeViewModel> children, bool truncated)
    {
        Children.Clear();
        foreach (var child in children)
            Children.Add(child);
        IsTruncated = truncated;
        IsLoaded = true;
        Error = null;
    }

    public void SetError(string message)
    {
        Children.Clear();
        Error = message;
        IsLoaded = false;
        Children.Add(new TreeNodeViewModel("載入失敗：" + message));
    }
}

public sealed partial class SearchResultViewModel : ObservableObject
{
    private readonly Action _checkedChanged;

    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _isMapped;

    public SearchResultViewModel(DaBrowseNode node, bool isMapped, Action checkedChanged)
    {
        Node = node;
        _isMapped = isMapped;
        _checkedChanged = checkedChanged;
    }

    public DaBrowseNode Node { get; }
    public string Name => Node.Name;
    public string ItemId => Node.ItemId;

    partial void OnIsCheckedChanged(bool value) => _checkedChanged();
}
