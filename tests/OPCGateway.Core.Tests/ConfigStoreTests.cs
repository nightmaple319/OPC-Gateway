using OPCGateway.Core.Configuration;
using Xunit;

namespace OPCGateway.Core.Tests;

public class ConfigStoreTests
{
    [Fact]
    public void Default_config_has_sample_tags_and_valid_values()
    {
        var config = ConfigStore.CreateDefault();
        Assert.NotEmpty(config.Tags);
        var result = ConfigStore.Validate(config);
        Assert.True(result.IsValid);
        Assert.Empty(result.Fixes);
    }

    [Fact]
    public void Validate_fixes_invalid_values_and_reports()
    {
        var config = new GatewayConfig();
        config.UaServer.Port = 0;
        config.UaServer.ApplicationUri = "urn with space";
        config.DaSource.UpdateRateMs = 1;
        config.UaServer.AllowNoSecurity = false;
        config.UaServer.EnableSecurity = false;

        var result = ConfigStore.Validate(config);

        Assert.True(result.IsValid);
        Assert.Equal(4840, config.UaServer.Port);
        Assert.Equal("urn:localhost:OPCGateway", config.UaServer.ApplicationUri);
        Assert.Equal(1000, config.DaSource.UpdateRateMs);
        Assert.True(config.UaServer.AllowNoSecurity);
        Assert.Equal(4, result.Fixes.Count);
    }

    [Theory]
    [InlineData(null, "System")]
    [InlineData("", "System")]
    [InlineData("dark", "Dark")]
    [InlineData("LIGHT", "Light")]
    [InlineData("auto", "System")]
    public void Validate_normalizes_theme(string? input, string expected)
    {
        var config = new GatewayConfig();
        config.Options.Theme = input!;
        var result = ConfigStore.Validate(config);
        Assert.Equal(expected, config.Options.Theme);
        Assert.Empty(result.Fixes);
    }

    [Fact]
    public void Validate_falls_back_to_system_theme_for_unknown_value()
    {
        var config = new GatewayConfig();
        config.Options.Theme = "sepia";
        var result = ConfigStore.Validate(config);
        Assert.Equal("System", config.Options.Theme);
        Assert.Single(result.Fixes);
    }

    [Fact]
    public void Validate_rejects_empty_progid()
    {
        var config = new GatewayConfig();
        config.DaSource.ProgId = " ";
        var result = ConfigStore.Validate(config);
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_removes_duplicate_and_empty_tags()
    {
        var config = new GatewayConfig();
        config.Tags.Add(new TagMappingConfig { ItemId = "A" });
        config.Tags.Add(new TagMappingConfig { ItemId = " A " });
        config.Tags.Add(new TagMappingConfig { ItemId = "" });
        config.Tags.Add(new TagMappingConfig { ItemId = "B", BrowseName = "  " });

        ConfigStore.Validate(config);

        Assert.Equal(2, config.Tags.Count);
        Assert.Null(config.Tags.Single(t => t.ItemId == "B").BrowseName);
    }

    [Fact]
    public void Parse_converts_legacy_format()
    {
        const string legacy = @"{
  ""OPCDAConfig"": { ""ServerName"": ""Matrikon.OPC.Simulation.1"", ""HostName"": ""localhost"", ""UpdateRate"": 500 },
  ""OPCUAConfig"": { ""ServerName"": ""Old Server"", ""Port"": 4841, ""EnableSecurity"": true, ""MaxClients"": 5 },
  ""ItemMappings"": [
    { ""OPCDAItemId"": ""Random.Int1"", ""OPCUANodeId"": ""Gateway.Random_Int1"", ""OPCUABrowseName"": ""Random_Int1"", ""IsEnabled"": false },
    { ""OPCDAItemId"": """" }
  ]
}";
        var store = new ConfigStore();
        var config = store.Parse(legacy);

        Assert.Equal("Matrikon.OPC.Simulation.1", config.DaSource.ProgId);
        Assert.Equal(500, config.DaSource.UpdateRateMs);
        Assert.Equal("Old Server", config.UaServer.ServerName);
        Assert.Equal(4841, config.UaServer.Port);
        Assert.True(config.UaServer.EnableSecurity);
        Assert.Equal(5, config.UaServer.MaxSessions);
        var tag = Assert.Single(config.Tags);
        Assert.Equal("Random.Int1", tag.ItemId);
        Assert.Equal("Random_Int1", tag.BrowseName);
        Assert.False(tag.Enabled);
    }

    [Fact]
    public void Save_and_Load_round_trip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "opcgw-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "cfg.json");
        try
        {
            var store = new ConfigStore(defaultPath: path);
            var config = ConfigStore.CreateDefault();
            config.UaServer.Port = 4850;
            config.Tags[0].AllowWrite = true;
            config.Options.AutoStart = true;

            store.Save(config);
            var loaded = store.Load();

            Assert.Equal(4850, loaded.UaServer.Port);
            Assert.True(loaded.Tags[0].AllowWrite);
            Assert.True(loaded.Options.AutoStart);
            Assert.Equal(config.Tags.Count, loaded.Tags.Count);

            var backup = store.Backup();
            Assert.NotNull(backup);
            Assert.True(File.Exists(backup));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_creates_default_when_missing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "opcgw-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "cfg.json");
        try
        {
            var store = new ConfigStore(defaultPath: path);
            var loaded = store.Load();
            Assert.True(File.Exists(path));
            Assert.NotEmpty(loaded.Tags);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
