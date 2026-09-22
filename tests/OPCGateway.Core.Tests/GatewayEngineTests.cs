using Opc.Ua;
using OPCGateway.Core.Configuration;
using OPCGateway.Core.Da;
using OPCGateway.Core.Engine;
using OPCGateway.Core.Tests.Fakes;
using Xunit;

namespace OPCGateway.Core.Tests;

public class GatewayEngineTests : IDisposable
{
    private readonly FakeDaClient _da = new();
    private readonly FakeUaServerHost _ua = new();
    private readonly GatewayEngine _engine;

    public GatewayEngineTests()
    {
        _engine = new GatewayEngine(_da, _ua);
    }

    public void Dispose() => _engine.Dispose();

    private static GatewayConfig MakeConfig(params string[] itemIds)
    {
        var config = new GatewayConfig();
        config.Options.UiRefreshIntervalMs = 50;
        foreach (var id in itemIds)
            config.Tags.Add(new TagMappingConfig { ItemId = id });
        return config;
    }

    [Fact]
    public async Task Start_creates_ua_nodes_and_subscribes_enabled_tags()
    {
        var config = MakeConfig("Random.Int1", "Random.Real4");
        config.Tags[1].Enabled = false;
        await _engine.ApplyConfigAsync(config);

        await _engine.StartAsync();

        Assert.Equal(GatewayRunState.Running, _engine.State);
        Assert.True(_ua.HasVariable("Random.Int1"));
        Assert.True(_ua.HasVariable("Random.Real4"));
        Assert.Equal(new[] { "Random.Int1" }, _da.SubscribedItems);
        Assert.Equal(StatusCodes.BadWaitingForInitialData, _ua.StatusOf("Random.Int1"));
        Assert.Equal(StatusCodes.BadOutOfService, _ua.StatusOf("Random.Real4"));
        Assert.Equal(TagState.WaitingForData, _engine.FindTag("Random.Int1")!.State);
        Assert.Equal(TagState.Disabled, _engine.FindTag("Random.Real4")!.State);
    }

    [Fact]
    public async Task Ua_variable_definition_mirrors_da_hierarchy()
    {
        await _engine.ApplyConfigAsync(MakeConfig("Channel1.Device1.Tag1"));
        await _engine.StartAsync();

        var def = _ua.Variables["Channel1.Device1.Tag1"];
        Assert.Equal(new[] { "Channel1", "Device1" }, def.FolderPath);
        Assert.Equal("Tag1", def.BrowseName);
    }

    [Fact]
    public async Task Da_values_flow_to_ua_with_mapped_status()
    {
        await _engine.ApplyConfigAsync(MakeConfig("Random.Int1"));
        await _engine.StartAsync();

        var received = new List<TagMapping>();
        _engine.TagsLiveChanged += (_, batch) => { lock (received) received.AddRange(batch); };

        _da.Push("Random.Int1", 42, DaQuality.Good);
        await Wait.UntilAsync(() => _ua.ValueOf("Random.Int1")?.Value is 42, "UA 收到值");

        var value = _ua.ValueOf("Random.Int1")!.Value;
        Assert.Equal(StatusCodes.Good, value.Status);

        var tag = _engine.FindTag("Random.Int1")!;
        Assert.Equal(42, tag.Value);
        Assert.Equal(TagState.Active, tag.State);
        Assert.Equal(typeof(int), tag.DataType);
        await Wait.UntilAsync(() => { lock (received) return received.Any(t => t.ItemId == "Random.Int1"); }, "批次通知");
        Assert.True(_engine.Statistics.TotalUpdates >= 1);

        _da.Push("Random.Int1", 7, 0x08);
        await Wait.UntilAsync(() => _ua.StatusOf("Random.Int1") == StatusCodes.BadNotConnected, "壞品質映射");
        Assert.Equal(1, _engine.Statistics.BadQualityUpdates);
    }

    [Fact]
    public async Task Da_disconnect_marks_nodes_bad_and_reconnect_resubscribes()
    {
        await _engine.ApplyConfigAsync(MakeConfig("Random.Int1"));
        await _engine.StartAsync();
        _da.Push("Random.Int1", 1);
        await Wait.UntilAsync(() => _ua.StatusOf("Random.Int1") == StatusCodes.Good, "初始值");

        _da.SimulateConnectionLost();

        Assert.Equal(StatusCodes.BadNotConnected, _ua.StatusOf("Random.Int1"));
        Assert.Equal(TagState.Idle, _engine.FindTag("Random.Int1")!.State);
        Assert.Equal(GatewayRunState.Degraded, _engine.State);

        _da.SimulateReconnected();
        await Wait.UntilAsync(() => _da.SubscribedItems.Contains("Random.Int1"), "重新訂閱");
        await Wait.UntilAsync(() => _engine.State == GatewayRunState.Running, "狀態恢復");
        Assert.Equal(StatusCodes.BadWaitingForInitialData, _ua.StatusOf("Random.Int1"));
    }

    [Fact]
    public async Task Toggling_enabled_reconciles_subscription()
    {
        await _engine.ApplyConfigAsync(MakeConfig("Random.Int1"));
        await _engine.StartAsync();
        var tag = _engine.FindTag("Random.Int1")!;

        tag.Enabled = false;
        await Wait.UntilAsync(() => !_da.SubscribedItems.Contains("Random.Int1"), "取消訂閱");
        await Wait.UntilAsync(() => _ua.StatusOf("Random.Int1") == StatusCodes.BadOutOfService, "停用狀態");

        tag.Enabled = true;
        await Wait.UntilAsync(() => _da.SubscribedItems.Contains("Random.Int1"), "重新訂閱");
    }

    [Fact]
    public async Task Disabled_tag_ignores_incoming_values()
    {
        var config = MakeConfig("Random.Int1");
        config.Tags[0].Enabled = false;
        await _engine.ApplyConfigAsync(config);
        await _engine.StartAsync();

        _da.Push("Random.Int1", 99);
        await Task.Delay(150);

        Assert.Null(_ua.ValueOf("Random.Int1"));
        Assert.Equal(0, _engine.Statistics.TotalUpdates);
    }

    [Fact]
    public async Task Write_back_requires_allow_write_and_connection()
    {
        var config = MakeConfig("W.Tag", "R.Tag");
        config.Tags[0].AllowWrite = true;
        await _engine.ApplyConfigAsync(config);
        await _engine.StartAsync();

        Assert.True(await _ua.WriteHandler!("W.Tag", 5));
        Assert.Single(_da.Writes);
        Assert.Equal(("W.Tag", (object?)5), _da.Writes[0]);

        Assert.False(await _ua.WriteHandler!("R.Tag", 5));
        Assert.False(await _ua.WriteHandler!("Unknown", 5));

        _da.WriteResult = unchecked((int)0x80004005);
        Assert.False(await _ua.WriteHandler!("W.Tag", 6));
    }

    [Fact]
    public async Task Subscribe_failure_marks_tag_error()
    {
        _da.InvalidItems.Add("Bad.Item");
        await _engine.ApplyConfigAsync(MakeConfig("Bad.Item", "Good.Item"));
        await _engine.StartAsync();

        var bad = _engine.FindTag("Bad.Item")!;
        Assert.Equal(TagState.Error, bad.State);
        Assert.Contains("Unknown", bad.ErrorMessage);
        Assert.Equal(StatusCodes.BadConfigurationError, _ua.StatusOf("Bad.Item"));
        Assert.Equal(TagState.WaitingForData, _engine.FindTag("Good.Item")!.State);
    }

    [Fact]
    public async Task Canonical_type_is_used_for_ua_datatype()
    {
        _da.ItemTypes["Random.Real4"] = typeof(float);
        await _engine.ApplyConfigAsync(MakeConfig("Random.Real4"));
        await _engine.StartAsync();

        Assert.Equal(typeof(float), _ua.Variables["Random.Real4"].ClrType);
        Assert.Equal("Float", _engine.FindTag("Random.Real4")!.DataTypeName);
    }

    [Fact]
    public async Task AddTags_and_RemoveTags_reconcile_immediately()
    {
        await _engine.ApplyConfigAsync(MakeConfig("A"));
        await _engine.StartAsync();

        var added = await _engine.AddTagsAsync(new[]
        {
            new TagMappingConfig { ItemId = "B" },
            new TagMappingConfig { ItemId = "A" },
        });

        Assert.Single(added);
        Assert.True(_ua.HasVariable("B"));
        Assert.Contains("B", _da.SubscribedItems);
        Assert.Equal(2, _engine.TagCount);

        await _engine.RemoveTagsAsync(new[] { "A" });
        Assert.False(_ua.HasVariable("A"));
        Assert.DoesNotContain("A", _da.SubscribedItems);
        Assert.Equal(1, _engine.TagCount);
    }

    [Fact]
    public async Task ApplyConfig_while_running_replaces_tags()
    {
        await _engine.ApplyConfigAsync(MakeConfig("A", "B"));
        await _engine.StartAsync();

        await _engine.ApplyConfigAsync(MakeConfig("B", "C"));

        Assert.False(_ua.HasVariable("A"));
        Assert.True(_ua.HasVariable("B"));
        Assert.True(_ua.HasVariable("C"));
        Assert.Equal(new[] { "B", "C" }, _da.SubscribedItems.OrderBy(x => x));
    }

    [Fact]
    public async Task Start_with_da_failure_is_degraded_and_ua_nodes_are_not_connected()
    {
        _da.FailConnect = true;
        await _engine.ApplyConfigAsync(MakeConfig("A"));

        await _engine.StartAsync();

        Assert.Equal(GatewayRunState.Degraded, _engine.State);
        Assert.True(_ua.IsRunning);
        Assert.Equal(StatusCodes.BadNotConnected, _ua.StatusOf("A"));
    }

    [Fact]
    public async Task Start_with_both_failures_throws_and_stops()
    {
        _da.FailConnect = true;
        _ua.FailStart = true;
        await _engine.ApplyConfigAsync(MakeConfig("A"));

        await Assert.ThrowsAsync<AggregateException>(() => _engine.StartAsync());
        Assert.Equal(GatewayRunState.Stopped, _engine.State);
    }

    [Fact]
    public async Task Stop_disconnects_everything_and_resets_state()
    {
        await _engine.ApplyConfigAsync(MakeConfig("A"));
        await _engine.StartAsync();

        await _engine.StopAsync();

        Assert.Equal(GatewayRunState.Stopped, _engine.State);
        Assert.False(_da.IsConnected);
        Assert.False(_ua.IsRunning);
        Assert.Equal(TagState.Idle, _engine.FindTag("A")!.State);
    }

    [Fact]
    public async Task BuildConfig_reflects_runtime_changes()
    {
        await _engine.ApplyConfigAsync(MakeConfig("A"));
        _engine.FindTag("A")!.Enabled = false;
        await _engine.AddTagsAsync(new[] { new TagMappingConfig { ItemId = "B", AllowWrite = true } });

        var config = _engine.BuildConfig();

        Assert.Equal(2, config.Tags.Count);
        Assert.False(config.Tags.Single(t => t.ItemId == "A").Enabled);
        Assert.True(config.Tags.Single(t => t.ItemId == "B").AllowWrite);
    }

    [Fact]
    public async Task Gateway_status_is_published_to_ua()
    {
        await _engine.ApplyConfigAsync(MakeConfig("A"));
        await _engine.StartAsync();

        await Wait.UntilAsync(() => _ua.Snapshots.Count > 0, "狀態快照");
        var snapshot = _ua.Snapshots.Last();
        Assert.True(snapshot.DaConnected);
        Assert.Equal(1, snapshot.TagCount);
    }
}
