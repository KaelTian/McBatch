using System.Reflection;

namespace McBatch;

internal sealed class ScannedPoint
{
    public ScannedPoint(PropertyInfo property, McAddress address)
    {
        Property = property;
        ClrType = property.PropertyType;
        Address = address;
    }

    public PropertyInfo Property { get; }

    public Type ClrType { get; }

    public McAddress Address { get; }

    /// <summary>占用的地址单元数: 位 1 点; short/ushort 1 字; int/uint/float 2 字。</summary>
    public int CellWidth => Address.IsBit ? 1 : ClrType == typeof(short) || ClrType == typeof(ushort) ? 1 : 2;
}

/// <summary>扫描对象类型上的 [McPoint] 属性, 并完成地址解析与类型映射校验。</summary>
internal static class PointScanner
{
    public static List<ScannedPoint> Scan(Type type)
    {
        var points = new List<ScannedPoint>();

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            McPointAttribute? attribute = property.GetCustomAttribute<McPointAttribute>();
            if (attribute is null)
                continue;

            McAddress address = McAddress.Parse(attribute.Address);
            ValidateMapping(property, address);
            points.Add(new ScannedPoint(property, address));
        }

        return points;
    }

    private static void ValidateMapping(PropertyInfo property, McAddress address)
    {
        Type type = property.PropertyType;

        if (type == typeof(bool))
        {
            if (address.Area == McArea.D)
                throw new ArgumentException(
                    $"属性 {property.Name}: bool 不能映射到 D 区(v1 不做位打包)。", nameof(property));
            return;
        }

        if (type == typeof(short) || type == typeof(ushort) ||
            type == typeof(int) || type == typeof(uint) || type == typeof(float))
        {
            if (address.Area != McArea.D)
                throw new ArgumentException(
                    $"属性 {property.Name}: 字类型 {type.Name} 只能映射到 D 区。", nameof(property));
            return;
        }

        throw new ArgumentException(
            $"属性 {property.Name}: 不支持的类型 {type.Name}。", nameof(property));
    }
}
