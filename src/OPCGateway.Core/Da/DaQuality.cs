namespace OPCGateway.Core.Da;

/// <summary>OPC DA 品質碼的主狀態（品質碼的高兩位元）。</summary>
public enum DaQualityMaster
{
    Bad = 0x00,
    Uncertain = 0x40,
    Good = 0xC0,
}

/// <summary>OPC DA 品質碼解析。品質碼是位元遮罩：QQSSSSLL（Q=主狀態、S=子狀態、L=限制）。</summary>
public static class DaQuality
{
    public const short Good = 0xC0;
    public const short Uncertain = 0x40;
    public const short Bad = 0x00;
    public const short BadNotConnected = 0x08;
    public const short BadWaitingForInitialData = 0x20;

    private const int MasterMask = 0xC0;
    private const int SubStatusMask = 0xFC;
    private const int LimitMask = 0x03;

    public static DaQualityMaster GetMaster(short quality)
    {
        var master = quality & MasterMask;
        return master switch
        {
            0xC0 => DaQualityMaster.Good,
            0x40 => DaQualityMaster.Uncertain,
            _ => DaQualityMaster.Bad,
        };
    }

    public static bool IsGood(short quality) => GetMaster(quality) == DaQualityMaster.Good;

    /// <summary>限制位元：0=無、1=低限、2=高限、3=常數。</summary>
    public static int GetLimit(short quality) => quality & LimitMask;

    /// <summary>取得含主狀態的子狀態碼（去除限制位元）。</summary>
    public static int GetSubStatus(short quality) => quality & SubStatusMask;

    /// <summary>可讀文字，例如「Good」「Bad (NotConnected)」「Good [High]」。</summary>
    public static string ToText(short quality)
    {
        var master = GetMaster(quality);
        var sub = GetSubStatus(quality);
        var limit = GetLimit(quality);

        var text = sub switch
        {
            0xC0 => "Good",
            0xD8 => "Good (LocalOverride)",
            0x40 => "Uncertain",
            0x44 => "Uncertain (LastUsableValue)",
            0x50 => "Uncertain (SensorNotAccurate)",
            0x54 => "Uncertain (EUExceeded)",
            0x58 => "Uncertain (SubNormal)",
            0x00 => "Bad",
            0x04 => "Bad (ConfigError)",
            0x08 => "Bad (NotConnected)",
            0x0C => "Bad (DeviceFailure)",
            0x10 => "Bad (SensorFailure)",
            0x14 => "Bad (LastKnownValue)",
            0x18 => "Bad (CommFailure)",
            0x1C => "Bad (OutOfService)",
            0x20 => "Bad (WaitingForInitialData)",
            _ => $"{master} (0x{sub:X2})",
        };

        return limit switch
        {
            1 => text + " [Low]",
            2 => text + " [High]",
            3 => text + " [Const]",
            _ => text,
        };
    }
}
