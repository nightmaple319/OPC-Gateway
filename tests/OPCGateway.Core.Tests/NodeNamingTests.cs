using OPCGateway.Core.Engine;
using Xunit;

namespace OPCGateway.Core.Tests;

public class NodeNamingTests
{
    [Fact]
    public void SplitPath_uses_separator()
    {
        Assert.Equal(new[] { "Random", "Int1" }, NodeNaming.SplitPath("Random.Int1", "."));
        Assert.Equal(new[] { "Channel1", "Device1", "Tag" }, NodeNaming.SplitPath("Channel1.Device1.Tag", "."));
        Assert.Equal(new[] { "Plant", "Line", "Tag" }, NodeNaming.SplitPath("Plant/Line/Tag", "/"));
    }

    [Fact]
    public void SplitPath_handles_edge_cases()
    {
        Assert.Empty(NodeNaming.SplitPath("", "."));
        Assert.Equal(new[] { "NoSeparator" }, NodeNaming.SplitPath("NoSeparator", "."));
        Assert.Equal(new[] { "A", "B" }, NodeNaming.SplitPath("A..B.", "."));
        Assert.Equal(new[] { "Whole.Id" }, NodeNaming.SplitPath("Whole.Id", ""));
    }

    [Fact]
    public void FolderSegments_exclude_leaf()
    {
        Assert.Equal(new[] { "Random" }, NodeNaming.GetFolderSegments("Random.Int1", "."));
        Assert.Empty(NodeNaming.GetFolderSegments("Int1", "."));
    }

    [Fact]
    public void DefaultBrowseName_is_last_segment()
    {
        Assert.Equal("Int1", NodeNaming.DefaultBrowseName("Random.Int1", "."));
        Assert.Equal("Saw-toothed Waves.Int1", NodeNaming.DefaultBrowseName("Saw-toothed Waves.Int1", "/"));
    }

    [Fact]
    public void SanitizeBrowseName_trims_and_falls_back()
    {
        Assert.Equal("Tag", NodeNaming.SanitizeBrowseName("  Tag\t", "fb"));
        Assert.Equal("fb", NodeNaming.SanitizeBrowseName("   ", "fb"));
        Assert.Equal("fb", NodeNaming.SanitizeBrowseName(null, "fb"));
    }
}
