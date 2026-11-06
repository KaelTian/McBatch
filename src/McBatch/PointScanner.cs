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

    /// <summary>
    /// 写场景扫描: 普通扫描后再校验任意两个属性覆盖的地址单元不得相交
    /// (SPEC §8.4: 同一地址、规范化后相同、多字点位重叠均不允许)。
    /// </summary>
    public static List<ScannedPoint> ScanForWrite(Type type)
    {
        List<ScannedPoint> points = Scan(type);
        ValidateWriteConflicts(points);
        return points;
    }

    private static void ValidateWriteConflicts(List<ScannedPoint> points)
    {
        var owner = new Dictionary<(McArea Area, bool IsBit, int Index), ScannedPoint>();
        foreach (ScannedPoint point in points)
        {
            for (int i = 0; i < point.CellWidth; i++)
            {
                var cell = (Area: point.Address.Area, IsBit: point.Address.IsBit, Index: point.Address.Number + i);
                if (owner.TryGetValue(cell, out ScannedPoint? existing))
                {
                    throw new ArgumentException(
                        $"属性 {existing.Property.Name} 与 {point.Property.Name} 覆盖同一地址单元 " +
                        $"{cell.Area}{(cell.IsBit ? "B" : "W")}{cell.Index}; " +
                        "写场景不允许地址冲突(SPEC §8.4)。");
                }

                owner[cell] = point;
            }
        }
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
