using System.Globalization;
using HslCommunication;
using HslCommunication.Profinet.Melsec;

namespace McBatch;

/// <summary>扫描、调度并串行执行读取, 把结果写入一个 new T() 后返回。</summary>
public static class McBatchReader
{
    public static T Read<T>(MelsecMcNet client) where T : new()
    {
        ArgumentNullException.ThrowIfNull(client);

        List<ScannedPoint> points = PointScanner.Scan(typeof(T));
        var instance = new T();

        // 空对象: 0 请求, 不触碰网络。
        if (points.Count == 0)
            return instance;

        IReadOnlyList<McRequest> plan = McBatchPlanner.BuildPlan(points);
        var words = new Dictionary<(McArea Area, int Index), ushort>();
        var bits = new Dictionary<(McArea Area, int Index), bool>();

        foreach (McRequest request in plan)
        {
            string address = FormatAddress(request.Area, request.Start);
            if (request.IsBit)
            {
                OperateResult<bool[]> result = client.ReadBool(address, (ushort)request.Count);
                if (!result.IsSuccess)
                    throw new InvalidOperationException(
                        $"读取位地址 {address} x{request.Count} 失败: {result.Message}");

                bool[] values = result.Content;
                for (int i = 0; i < request.Count; i++)
                    bits[(request.Area, request.Start + i)] = values[i];
            }
            else
            {
                OperateResult<byte[]> result = client.Read(address, (ushort)request.Count);
                if (!result.IsSuccess)
                    throw new InvalidOperationException(
                        $"读取字地址 {address} x{request.Count} 失败: {result.Message}");

                byte[] content = result.Content;
                for (int i = 0; i < request.Count; i++)
                    words[(request.Area, request.Start + i)] = BitConverter.ToUInt16(content, i * 2);
            }
        }

        foreach (ScannedPoint point in points)
            point.Property.SetValue(instance, ExtractValue(point, words, bits));

        return instance;
    }

    private static object ExtractValue(
        ScannedPoint point,
        Dictionary<(McArea Area, int Index), ushort> words,
        Dictionary<(McArea Area, int Index), bool> bits)
    {
        McArea area = point.Address.Area;
        int address = point.Address.Number;

        if (point.ClrType == typeof(bool))
            return bits[(area, address)];

        ushort low = words[(area, address)];
        if (point.ClrType == typeof(short))
            return (short)low;
        if (point.ClrType == typeof(ushort))
            return low;

        // int/uint/float: 低地址字为低 16 位。
        ushort high = words[(area, address + 1)];
        uint raw = (uint)low | ((uint)high << 16);
        if (point.ClrType == typeof(int))
            return unchecked((int)raw);
        if (point.ClrType == typeof(uint))
            return raw;

        return BitConverter.Int32BitsToSingle(unchecked((int)raw));
    }

    private static string FormatAddress(McArea area, int start) =>
        $"{area}{start.ToString(CultureInfo.InvariantCulture)}";
}
