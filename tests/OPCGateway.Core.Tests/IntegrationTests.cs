using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua;
using OPCGateway.Core.Configuration;
using OPCGateway.Core.Da;
using OPCGateway.Core.Ua;
using Xunit;

namespace OPCGateway.Core.Tests;

/// <summary>
/// 需要本機 Matrikon OPC Simulation Server 與 OPC Core Components（32 位元）。
/// 執行方式：set OPCGW_INTEGRATION=1 後 dotnet test。
/// </summary>
public class IntegrationTests
{
    [IntegrationFact]
    public async Task Titanium_client_connects_browses_and_receives_values()
    {
        using var client = new TitaniumDaClient(NullLogger<TitaniumDaClient>.Instance);
        var config = new DaSourceConfig { ProgId = "Matrikon.OPC.Simulation.1", Host = "localhost", UpdateRateMs = 200 };

        await client.ConnectAsync(config);
        Assert.True(client.IsConnected);

        var root = await client.BrowseAsync(null, 100);
        Assert.NotEmpty(root.Nodes);

        var servers = await client.EnumerateServersAsync("localhost");
        Assert.Contains(servers, s => s.ProgId.StartsWith("Matrikon", StringComparison.OrdinalIgnoreCase));

        var received = new List<DaValue>();
        client.ValuesChanged += (_, values) => { lock (received) received.AddRange(values); };

        var results = await client.SubscribeAsync(new[] { "Random.Int1", "Not.A.Real.Item" });
        Assert.True(results.Single(r => r.ItemId == "Random.Int1").Success);
        Assert.False(results.Single(r => r.ItemId == "Not.A.Real.Item").Success);
        // Matrikon 的 Int1 是單位元組整數（VT_I1）
        Assert.Equal(typeof(sbyte), results.Single(r => r.ItemId == "Random.Int1").CanonicalType);

        // 第一筆通常是 BadOutOfService，之後才是 Good；兩者都要收到
        await Wait.UntilAsync(() => { lock (received) return received.Count(v => v.ItemId == "Random.Int1" && DaQuality.IsGood(v.Quality)) >= 3; },
            () => { lock (received) return $"收到 Good 的 Random.Int1（目前 {received.Count} 筆：{string.Join(",", received.Select(v => DaQuality.ToText(v.Quality)))}）"; }, 10000);

        var status = await client.GetStatusAsync();
        Assert.Equal(DaServerState.Running, status!.State);

        await client.DisconnectAsync();
        Assert.False(client.IsConnected);
    }

    [IntegrationFact]
    public async Task Ua_server_host_serves_updated_values_to_a_client()
    {
        using var host = new UaServerHost(NullLogger<UaServerHost>.Instance);
        var config = new UaServerConfig { Port = 48400, ApplicationName = "OPC Gateway Host Test", ApplicationUri = "urn:localhost:OPCGatewayHostTest" };

        await host.StartAsync(config);
        Assert.True(host.IsRunning);
        Assert.NotEmpty(host.EndpointUrls);

        host.AddOrUpdateVariable(new UaVariableDefinition("Test.Tag", new[] { "Test" }, "Tag", null, typeof(int), false));
        host.UpdateValue("Test.Tag", 5, StatusCodes.Good, DateTime.UtcNow);

        var session = await UaTestClient.ConnectAsync("opc.tcp://localhost:48400");
        try
        {
            var nsIndex = (ushort)session.NamespaceUris.GetIndex(config.NamespaceUri);
            var node = new NodeId("Test.Tag", nsIndex);

            var read = await UaTestClient.ReadAsync(session, node);
            Assert.True(StatusCode.IsGood(read[0].StatusCode), $"第一次讀取狀態 {read[0].StatusCode}");
            Assert.Equal(5, read[0].Value);

            host.UpdateValue("Test.Tag", 6, StatusCodes.Good, DateTime.UtcNow);
            read = await UaTestClient.ReadAsync(session, node);
            Assert.Equal(6, read[0].Value);

            host.UpdateValue("Test.Tag", 7, StatusCodes.UncertainLastUsableValue, DateTime.UtcNow);
            read = await UaTestClient.ReadAsync(session, node);
            Assert.Equal(StatusCodes.UncertainLastUsableValue, read[0].StatusCode.Code);
            Assert.Equal(7, read[0].Value);

            host.SetStatus("Test.Tag", StatusCodes.BadNotConnected);
            read = await UaTestClient.ReadAsync(session, node);
            Assert.Equal(StatusCodes.BadNotConnected, read[0].StatusCode.Code);

            host.SetAllStatus(StatusCodes.BadOutOfService);
            read = await UaTestClient.ReadAsync(session, node);
            Assert.Equal(StatusCodes.BadOutOfService, read[0].StatusCode.Code);

            host.UpdateValue("Test.Tag", 8, StatusCodes.Good, DateTime.UtcNow);
            read = await UaTestClient.ReadAsync(session, node);
            Assert.True(StatusCode.IsGood(read[0].StatusCode), $"恢復 Good 後狀態 {read[0].StatusCode}");
            Assert.Equal(8, read[0].Value);

            var children = await UaTestClient.BrowseNamesAsync(session, new NodeId("__folder__/Gateway", nsIndex));
            Assert.Contains("Test", children);
            Assert.Contains("Status", children);

            await Wait.UntilAsync(() => host.Clients.Count == 1, "主機看見客戶端", 10000);
            Assert.Equal(UaTestClient.ApplicationName, host.Clients[0].ApplicationName);
        }
        finally
        {
            await UaTestClient.CloseAsync(session);
        }

        await host.StopAsync();
        Assert.False(host.IsRunning);
    }
}
