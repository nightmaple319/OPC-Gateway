using Opc.Ua;
using OPCGateway.Core.Da;

namespace OPCGateway.Core.Ua;

/// <summary>OPC DA 品質碼轉 OPC UA StatusCode（依 OPC UA Part 8 的對應表），並保留限制位元。</summary>
public static class UaStatusMapper
{
    private const uint LimitBitsShift = 8;

    public static uint FromDaQuality(short quality)
    {
        var sub = DaQuality.GetSubStatus(quality);
        var limit = (uint)DaQuality.GetLimit(quality);

        uint code = sub switch
        {
            0xC0 => StatusCodes.Good,
            0xD8 => StatusCodes.GoodLocalOverride,
            0x40 => StatusCodes.Uncertain,
            0x44 => StatusCodes.UncertainLastUsableValue,
            0x50 => StatusCodes.UncertainSensorNotAccurate,
            0x54 => StatusCodes.UncertainEngineeringUnitsExceeded,
            0x58 => StatusCodes.UncertainSubNormal,
            0x00 => StatusCodes.Bad,
            0x04 => StatusCodes.BadConfigurationError,
            0x08 => StatusCodes.BadNotConnected,
            0x0C => StatusCodes.BadDeviceFailure,
            0x10 => StatusCodes.BadSensorFailure,
            0x14 => StatusCodes.BadNoCommunication,
            0x18 => StatusCodes.BadCommunicationError,
            0x1C => StatusCodes.BadOutOfService,
            0x20 => StatusCodes.BadWaitingForInitialData,
            _ => DaQuality.GetMaster(quality) switch
            {
                DaQualityMaster.Good => StatusCodes.Good,
                DaQualityMaster.Uncertain => StatusCodes.Uncertain,
                _ => StatusCodes.Bad,
            },
        };

        return code | (limit << (int)LimitBitsShift);
    }

    public static bool IsGood(uint statusCode) => StatusCode.IsGood(statusCode);
    public static bool IsBad(uint statusCode) => StatusCode.IsBad(statusCode);
    public static bool IsUncertain(uint statusCode) => StatusCode.IsUncertain(statusCode);

    private static readonly Lazy<Dictionary<uint, string>> SymbolicNames = new(BuildSymbolicNames);

    private static Dictionary<uint, string> BuildSymbolicNames()
    {
        var table = new Dictionary<uint, string>();
        foreach (var field in typeof(StatusCodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (field.FieldType != typeof(uint))
                continue;
            var value = (uint)field.GetValue(null)!;
            if (!table.ContainsKey(value))
                table[value] = field.Name;
        }
        return table;
    }

    /// <summary>StatusCode 的符號名稱（例如 BadNotConnected），找不到就回傳十六進位。</summary>
    public static string ToText(uint statusCode)
    {
        var code = statusCode & 0xFFFF0000;
        if (!SymbolicNames.Value.TryGetValue(code, out var name) || string.IsNullOrEmpty(name))
            return $"0x{statusCode:X8}";

        var limit = (statusCode >> (int)LimitBitsShift) & 0x3;
        return limit switch
        {
            1 => name + " [Low]",
            2 => name + " [High]",
            3 => name + " [Const]",
            _ => name,
        };
    }
}
