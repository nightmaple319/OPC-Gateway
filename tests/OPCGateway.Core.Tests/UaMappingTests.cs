using Opc.Ua;
using OPCGateway.Core.Ua;
using Xunit;

namespace OPCGateway.Core.Tests;

public class UaStatusMapperTests
{
    [Theory]
    [InlineData(0xC0, StatusCodes.Good)]
    [InlineData(0xD8, StatusCodes.GoodLocalOverride)]
    [InlineData(0x40, StatusCodes.Uncertain)]
    [InlineData(0x44, StatusCodes.UncertainLastUsableValue)]
    [InlineData(0x00, StatusCodes.Bad)]
    [InlineData(0x04, StatusCodes.BadConfigurationError)]
    [InlineData(0x08, StatusCodes.BadNotConnected)]
    [InlineData(0x0C, StatusCodes.BadDeviceFailure)]
    [InlineData(0x18, StatusCodes.BadCommunicationError)]
    [InlineData(0x1C, StatusCodes.BadOutOfService)]
    [InlineData(0x20, StatusCodes.BadWaitingForInitialData)]
    public void FromDaQuality_maps_substatus(int quality, uint expected)
    {
        Assert.Equal(expected, UaStatusMapper.FromDaQuality((short)quality));
    }

    [Fact]
    public void FromDaQuality_preserves_limit_bits()
    {
        var code = UaStatusMapper.FromDaQuality(0xC2);
        Assert.True(StatusCode.IsGood(code));
        Assert.Equal(0x200u, code & 0x300u);
        Assert.Equal("Good [High]", UaStatusMapper.ToText(code));
    }

    [Fact]
    public void ToText_uses_symbolic_names()
    {
        Assert.Equal("BadNotConnected", UaStatusMapper.ToText(StatusCodes.BadNotConnected));
        Assert.Equal("Good", UaStatusMapper.ToText(StatusCodes.Good));
    }
}

public class UaTypeMapperTests
{
    [Theory]
    [InlineData(typeof(bool), "Boolean")]
    [InlineData(typeof(int), "Int32")]
    [InlineData(typeof(short), "Int16")]
    [InlineData(typeof(float), "Float")]
    [InlineData(typeof(double), "Double")]
    [InlineData(typeof(decimal), "Double")]
    [InlineData(typeof(string), "String")]
    [InlineData(typeof(DateTime), "DateTime")]
    public void Scalar_types_map_to_builtin_datatypes(Type clr, string expectedFieldName)
    {
        var expected = (NodeId)typeof(DataTypeIds).GetField(expectedFieldName)!.GetValue(null)!;
        var id = UaTypeMapper.GetDataTypeId(clr, out var rank);
        Assert.Equal(ValueRanks.Scalar, rank);
        Assert.Equal(expected, id);
    }

    [Fact]
    public void Arrays_map_to_one_dimension()
    {
        var id = UaTypeMapper.GetDataTypeId(typeof(double[]), out var rank);
        Assert.Equal(DataTypeIds.Double, id);
        Assert.Equal(ValueRanks.OneDimension, rank);
    }

    [Fact]
    public void ByteArray_is_bytestring_scalar()
    {
        var id = UaTypeMapper.GetDataTypeId(typeof(byte[]), out var rank);
        Assert.Equal(DataTypeIds.ByteString, id);
        Assert.Equal(ValueRanks.Scalar, rank);
    }

    [Fact]
    public void Unknown_or_null_is_basedatatype()
    {
        Assert.Equal(DataTypeIds.BaseDataType, UaTypeMapper.GetDataTypeId(null, out _));
        Assert.Equal(DataTypeIds.BaseDataType, UaTypeMapper.GetDataTypeId(typeof(object), out _));
    }

    [Fact]
    public void NormalizeValue_converts_variant_only_types()
    {
        Assert.Equal(1.5, UaTypeMapper.NormalizeValue(1.5m));
        Assert.Equal("x", UaTypeMapper.NormalizeValue('x'));
        var typed = UaTypeMapper.NormalizeValue(new object[] { 1, 2, 3 });
        Assert.IsType<int[]>(typed);
        var mixed = UaTypeMapper.NormalizeValue(new object[] { 1, "a" });
        Assert.IsType<object[]>(mixed);
        Assert.Null(UaTypeMapper.NormalizeValue(null));
    }

    [Fact]
    public void DisplayName_is_friendly()
    {
        Assert.Equal("Int32", UaTypeMapper.GetTypeDisplayName(typeof(int)));
        Assert.Equal("Float", UaTypeMapper.GetTypeDisplayName(typeof(float)));
        Assert.Equal("Double[]", UaTypeMapper.GetTypeDisplayName(typeof(double[])));
        Assert.Equal("ByteString", UaTypeMapper.GetTypeDisplayName(typeof(byte[])));
        Assert.Equal("?", UaTypeMapper.GetTypeDisplayName(null));
    }
}
