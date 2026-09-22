using Xunit;

namespace OPCGateway.Core.Tests;

internal static class Wait
{
    /// <summary>輪詢直到條件成立或逾時；逾時時以 Assert 失敗並附上說明。</summary>
    public static Task UntilAsync(Func<bool> condition, string description, int timeoutMs = 5000)
        => UntilAsync(condition, () => description, timeoutMs);

    /// <summary>同上，但說明在逾時當下才產生，可帶入即時狀態。</summary>
    public static async Task UntilAsync(Func<bool> condition, Func<string> description, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;
            await Task.Delay(25);
        }
        Assert.True(condition(), $"等待逾時: {description()}");
    }
}

/// <summary>只有設定環境變數 OPCGW_INTEGRATION=1 時才執行的整合測試（需要本機 Matrikon 模擬伺服器）。</summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("OPCGW_INTEGRATION") != "1")
            Skip = "設定環境變數 OPCGW_INTEGRATION=1 並安裝 Matrikon OPC Simulation Server 後才會執行。";
    }
}
