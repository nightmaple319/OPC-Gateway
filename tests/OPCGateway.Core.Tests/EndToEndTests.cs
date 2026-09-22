using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using OPCGateway.Core.Configuration;
using OPCGateway.Core.Da;
using OPCGateway.Core.Engine;
using OPCGateway.Core.Ua;
using Xunit;

namespace OPCGateway.Core.Tests;

/// <summary>
/// 真正的端到端：TitaniumDaClient 連 Matrikon → GatewayEngine → UaServerHost，
/// 再以 OPC UA 客戶端連進閘道讀值、看階層、看狀態節點。需要 OPCGW_INTEGRATION=1。
/// </summary>
public class EndToEndTests
{
    private const int Port = 48401;

    [IntegrationFact]
    public async Task Values_from_matrikon_are_readable_through_ua_client()
    {
        using var da = new TitaniumDaClient(NullLogger<TitaniumDaClient>.Instance);
        using var ua = new UaServerHost(NullLogger<UaServerHost>.Instance);
        using var engine = new GatewayEngine(da, ua, NullLogger<GatewayEngine>.Instance);

        var config = new GatewayConfig();
        config.DaSource.ProgId = "Matrikon.OPC.Simulation.1";
        config.DaSource.UpdateRateMs = 200;
        config.UaServer.Port = Port;
        config.UaServer.ApplicationName = "OPC Gateway E2E";
        config.UaServer.ApplicationUri = "urn:localhost:OPCGatewayE2E";
        config.Options.UiRefreshIntervalMs = 50;
        config.Tags.Add(new TagMappingConfig { ItemId = "Random.Int1" });
        config.Tags.Add(new TagMappingConfig { ItemId = "Random.Real4" });
        config.Tags.Add(new TagMappingConfig { ItemId = "Not.A.Real.Item" });

        await engine.ApplyConfigAsync(config);
        await engine.StartAsync();
        Assert.Equal(GatewayRunState.Running, engine.State);

        var intTag = engine.FindTag("Random.Int1")!;
        var realTag = engine.FindTag("Random.Real4")!;

        // Matrikon 的第一筆回呼是 BadOutOfService，之後才是 Good；引擎必須持續套用後續的值。
        await Wait.UntilAsync(() => intTag.UpdateCount >= 3 && intTag.QualityMaster == DaQualityMaster.Good,
            () => $"Random.Int1 品質變 Good（目前 {intTag.QualityText}，更新 {intTag.UpdateCount} 次，UA {intTag.UaStatusText}）", 10000);
        await Wait.UntilAsync(() => realTag.UpdateCount >= 3 && realTag.QualityMaster == DaQualityMaster.Good,
            () => $"Random.Real4 品質變 Good（目前 {realTag.QualityText}，更新 {realTag.UpdateCount} 次）", 10000);
        Assert.Equal(TagState.Active, intTag.State);
        Assert.Equal(TagState.Error, engine.FindTag("Not.A.Real.Item")!.State);

        var session = await UaTestClient.ConnectAsync($"opc.tcp://localhost:{Port}");
        try
        {
            var ns = session.NamespaceUris.GetIndex(config.UaServer.NamespaceUri);
            Assert.True(ns > 0, "找不到閘道命名空間");
            var nsIndex = (ushort)ns;

            var values = await UaTestClient.ReadAsync(session,
                new NodeId("Random.Int1", nsIndex),
                new NodeId("Random.Real4", nsIndex),
                new NodeId("Not.A.Real.Item", nsIndex),
                new NodeId("__folder__/Gateway/Status/DaConnected", nsIndex),
                new NodeId("__folder__/Gateway/Status/TagCount", nsIndex));

            Assert.True(StatusCode.IsGood(values[0].StatusCode),
                $"Random.Int1 UA 狀態 {values[0].StatusCode}，引擎端 {intTag.UaStatusText} / {intTag.QualityText} / 更新 {intTag.UpdateCount} 次");
            Assert.IsType<sbyte>(values[0].Value);
            Assert.True(StatusCode.IsGood(values[1].StatusCode), $"Random.Real4 UA 狀態 {values[1].StatusCode}");
            Assert.IsType<float>(values[1].Value);
            Assert.Equal(StatusCodes.BadConfigurationError, values[2].StatusCode.Code);
            Assert.Equal(true, values[3].Value);
            Assert.Equal(3, values[4].Value);

            var first = (sbyte)values[0].Value;
            await Wait.UntilAsync(() =>
            {
                var again = UaTestClient.ReadAsync(session, new NodeId("Random.Int1", nsIndex)).GetAwaiter().GetResult();
                return (sbyte)again[0].Value != first;
            }, "Random.Int1 的值會變動", 10000);

            var gatewayChildren = await UaTestClient.BrowseNamesAsync(session, new NodeId("__folder__/Gateway", nsIndex));
            Assert.Contains("Random", gatewayChildren);
            Assert.Contains("Status", gatewayChildren);
            Assert.Contains("Not", gatewayChildren);

            var randomChildren = await UaTestClient.BrowseNamesAsync(session, new NodeId("__folder__/Gateway/Random", nsIndex));
            Assert.Contains("Int1", randomChildren);
            Assert.Contains("Real4", randomChildren);

            await Wait.UntilAsync(() => engine.Clients.Count == 1, "閘道看見客戶端", 10000);
            Assert.Equal(UaTestClient.ApplicationName, engine.Clients[0].ApplicationName);
        }
        finally
        {
            await UaTestClient.CloseAsync(session);
        }

        await engine.StopAsync();
        Assert.Equal(GatewayRunState.Stopped, engine.State);
    }
}
