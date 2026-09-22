using Opc.Ua;

namespace OPCGateway.Core.Ua;

/// <summary>CLR 型別（來自 OPC DA 的 VARIANT）與 OPC UA DataType 之間的對應。</summary>
public static class UaTypeMapper
{
    /// <summary>取得 UA DataType NodeId 與 ValueRank。未知型別回傳 BaseDataType。</summary>
    public static NodeId GetDataTypeId(Type? clrType, out int valueRank)
    {
        valueRank = ValueRanks.Scalar;
        if (clrType == null)
            return DataTypeIds.BaseDataType;

        clrType = Nullable.GetUnderlyingType(clrType) ?? clrType;

        if (clrType == typeof(byte[]))
            return DataTypeIds.ByteString;

        if (clrType.IsArray)
        {
            valueRank = ValueRanks.OneDimension;
            var element = clrType.GetElementType();
            return element == null ? DataTypeIds.BaseDataType : GetScalarDataTypeId(element);
        }

        return GetScalarDataTypeId(clrType);
    }

    private static NodeId GetScalarDataTypeId(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(bool)) return DataTypeIds.Boolean;
        if (type == typeof(sbyte)) return DataTypeIds.SByte;
        if (type == typeof(byte)) return DataTypeIds.Byte;
        if (type == typeof(short)) return DataTypeIds.Int16;
        if (type == typeof(ushort)) return DataTypeIds.UInt16;
        if (type == typeof(int)) return DataTypeIds.Int32;
        if (type == typeof(uint)) return DataTypeIds.UInt32;
        if (type == typeof(long)) return DataTypeIds.Int64;
        if (type == typeof(ulong)) return DataTypeIds.UInt64;
        if (type == typeof(float)) return DataTypeIds.Float;
        if (type == typeof(double)) return DataTypeIds.Double;
        if (type == typeof(decimal)) return DataTypeIds.Double;
        if (type == typeof(string)) return DataTypeIds.String;
        if (type == typeof(char)) return DataTypeIds.String;
        if (type == typeof(DateTime)) return DataTypeIds.DateTime;
        if (type == typeof(DateTimeOffset)) return DataTypeIds.DateTime;
        if (type == typeof(Guid)) return DataTypeIds.Guid;

        return DataTypeIds.BaseDataType;
    }

    /// <summary>把 DA 送來的值轉成 UA Variant 能承載的型別（decimal 轉 double、char 轉 string 等）。</summary>
    public static object? NormalizeValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case decimal d:
                return (double)d;
            case char c:
                return c.ToString();
            case DateTimeOffset dto:
                return dto.UtcDateTime;
            case DateTime dt:
                return dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
            case decimal[] decimals:
                return decimals.Select(x => (double)x).ToArray();
            case char[] chars:
                return chars.Select(x => x.ToString()).ToArray();
            case DateTimeOffset[] dtos:
                return dtos.Select(x => x.UtcDateTime).ToArray();
            case object[] objects:
                return NormalizeObjectArray(objects);
            default:
                return value;
        }
    }

    /// <summary>VT_ARRAY|VT_VARIANT 會以 object[] 到達；若元素型別一致就轉成強型別陣列。</summary>
    private static object NormalizeObjectArray(object[] objects)
    {
        if (objects.Length == 0)
            return objects;

        var normalized = objects.Select(NormalizeValue).ToArray();
        var first = normalized.FirstOrDefault(x => x != null)?.GetType();
        if (first == null || normalized.Any(x => x == null || x.GetType() != first))
            return normalized;

        var typed = Array.CreateInstance(first, normalized.Length);
        Array.Copy(normalized, typed, normalized.Length);
        return typed;
    }

    /// <summary>以 OPC UA 的型別名稱顯示，例如 Int32、Float、Double[]、ByteString。</summary>
    public static string GetTypeDisplayName(Type? type)
    {
        if (type == null)
            return "?";
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(byte[]))
            return "ByteString";
        if (type.IsArray)
            return GetTypeDisplayName(type.GetElementType()) + "[]";
        if (type == typeof(float))
            return "Float";
        if (type == typeof(decimal))
            return "Double";
        if (type == typeof(char))
            return "String";
        if (type == typeof(DateTimeOffset))
            return "DateTime";
        if (type == typeof(object))
            return "Variant";
        return type.Name;
    }
}
