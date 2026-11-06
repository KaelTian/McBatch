using System.Globalization;
using HslCommunication;
using HslCommunication.Profinet.Melsec;

namespace McBatch;

/// <summary>扫描、调度并串行执行写入; 调度与读共用 McBatchPlanner.BuildPlan(SPEC §8.2)。</summary>
public static class McBatchWriter
{
    public static void Write<T>(MelsecMcNet client, T instance) where T : new()
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(instance);

        List<ScannedPoint> points = PointScanner.ScanForWrite(typeof(T));

        // 空对象: 0 请求, 不触碰网络(SPEC §8.3 A10)。
        if (points.Count == 0)
            return;

        IReadOnlyList<McRequest> plan = McBatchPlanner.BuildPlan(points);

        var words = new Dictionary<(McArea Area, int Index), ushort>();
        var bits = new Dictionary<(McArea Area, int Index), bool>();
        CollectValues(points, instance, words, bits);

        foreach (McRequest request in plan)
        {
            string address = FormatAddress(request.Area, request.Start);
            if (request.IsBit)
            {
                var values = new bool[request.Count];
                for (int i = 0; i < request.Count; i++)
                    values[i] = bits[(request.Area, request.Start + i)];

                OperateResult result = client.Write(address, values);
                if (!result.IsSuccess)
                    throw new InvalidOperationException(
                        $"写入位地址 {address} x{request.Count} 失败: {result.Message}");
            }
            else
            {
                var content = new byte[request.Count * 2];
                for (int i = 0; i < request.Count; i++)
                {
                    ushort word = words[(request.Area, request.Start + i)];
                    content[i * 2] = (byte)word;
                    content[i * 2 + 1] = (byte)(word >> 8);
                }

                OperateResult result = client.Write(address, content);
                if (!result.IsSuccess)
                    throw new InvalidOperationException(
                        $"写入字地址 {address} x{request.Count} 失败: {result.Message}");
            }
        }
    }

    private static void CollectValues(
        IReadOnlyList<ScannedPoint> points,
        object instance,
        Dictionary<(McArea Area, int Index), ushort> words,
        Dictionary<(McArea Area, int Index), bool> bits)
    {
        foreach (ScannedPoint point in points)
        {
            object? value = point.Property.GetValue(instance);
            McArea area = point.Address.Area;
            int number = point.Address.Number;

            if (point.ClrType == typeof(bool))
            {
                bits[(area, number)] = (bool)value!;
                continue;
            }

            if (point.ClrType == typeof(short) || point.ClrType == typeof(ushort))
            {
                words[(area, number)] = point.ClrType == typeof(short)
                    ? unchecked((ushort)(short)value!)
                    : (ushort)value!;
                continue;
            }

            // int/uint/float: 低地址字为低 16 位(SPEC §2.2)。
            uint raw = point.ClrType switch
            {
                Type t when t == typeof(int) => unchecked((uint)(int)value!),
                Type t when t == typeof(uint) => (uint)value!,
                Type t when t == typeof(float) => unchecked((uint)BitConverter.SingleToInt32Bits((float)value!)),
                _ => throw new InvalidOperationException($"不支持的类型 {point.ClrType.Name}。"),
            };
            words[(area, number)] = (ushort)(raw & 0xFFFF);
            words[(area, number + 1)] = (ushort)(raw >> 16);
        }
    }

    private static string FormatAddress(McArea area, int start) =>
        $"{area}{start.ToString(CultureInfo.InvariantCulture)}";
}
