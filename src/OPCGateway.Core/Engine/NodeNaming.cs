namespace OPCGateway.Core.Engine;

/// <summary>DA ItemId 與 UA 節點命名之間的轉換規則。</summary>
public static class NodeNaming
{
    /// <summary>依分隔符號切出階層片段；空片段會被去除。</summary>
    public static string[] SplitPath(string itemId, string separator)
    {
        if (string.IsNullOrEmpty(itemId))
            return Array.Empty<string>();
        if (string.IsNullOrEmpty(separator))
            return new[] { itemId };

        return itemId
            .Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToArray();
    }

    /// <summary>資料夾片段（不含最後一段）。</summary>
    public static string[] GetFolderSegments(string itemId, string separator)
    {
        var parts = SplitPath(itemId, separator);
        return parts.Length <= 1 ? Array.Empty<string>() : parts.Take(parts.Length - 1).ToArray();
    }

    /// <summary>預設 BrowseName：ItemId 的最後一段；切不出來就用整個 ItemId。</summary>
    public static string DefaultBrowseName(string itemId, string separator)
    {
        var parts = SplitPath(itemId, separator);
        return parts.Length == 0 ? itemId : parts[parts.Length - 1];
    }

    /// <summary>
    /// 整理 BrowseName：去除前後空白與控制字元；空字串回退為 fallback。
    /// UA 的 BrowseName 允許空白與符號，因此不做多餘替換，以保留原始可讀性。
    /// </summary>
    public static string SanitizeBrowseName(string? name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
            return fallback;

        var cleaned = new string(name!.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length == 0 ? fallback : cleaned;
    }
}
