using OPCGateway.Core.Da;
using Xunit;

namespace OPCGateway.Core.Tests;

public class DaQualityTests
{
    [Theory]
    [InlineData(0xC0, DaQualityMaster.Good)]
    [InlineData(0xC1, DaQualityMaster.Good)]
    [InlineData(0xD8, DaQualityMaster.Good)]
    [InlineData(0x40, DaQualityMaster.Uncertain)]
    [InlineData(0x44, DaQualityMaster.Uncertain)]
    [InlineData(0x00, DaQualityMaster.Bad)]
    [InlineData(0x08, DaQualityMaster.Bad)]
    [InlineData(0x80, DaQualityMaster.Bad)]
    public void GetMaster_masks_high_bits(int quality, DaQualityMaster expected)
    {
        Assert.Equal(expected, DaQuality.GetMaster((short)quality));
    }

    [Theory]
    [InlineData(0xC0, "Good")]
    [InlineData(0xC1, "Good [Low]")]
    [InlineData(0xC2, "Good [High]")]
    [InlineData(0xC3, "Good [Const]")]
    [InlineData(0xD8, "Good (LocalOverride)")]
    [InlineData(0x08, "Bad (NotConnected)")]
    [InlineData(0x20, "Bad (WaitingForInitialData)")]
    [InlineData(0x44, "Uncertain (LastUsableValue)")]
    public void ToText_describes_substatus_and_limit(int quality, string expected)
    {
        Assert.Equal(expected, DaQuality.ToText((short)quality));
    }

    [Fact]
    public void Limit_bits_are_extracted()
    {
        Assert.Equal(0, DaQuality.GetLimit(0xC0));
        Assert.Equal(1, DaQuality.GetLimit(0xC1));
        Assert.Equal(3, DaQuality.GetLimit(0x43));
    }
}
