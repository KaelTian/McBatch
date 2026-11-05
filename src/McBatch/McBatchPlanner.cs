namespace McBatch;

/// <summary>SPEC §3 的批量调度: 分组 → 升序 → 合并连续段 → 按协议上限拆包。</summary>
public static class McBatchPlanner
{
    // SPEC §3.3: 字 ≤960 字/请求; 位 ≤7168 点/请求。
    private const int WordLimit = 960;
    private const int BitLimit = 7168;

    public static IReadOnlyList<McRequest> Plan(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return BuildPlan(PointScanner.Scan(target.GetType()));
    }

    internal static IReadOnlyList<McRequest> BuildPlan(IReadOnlyList<ScannedPoint> points)
    {
        var cells = new HashSet<(McArea Area, bool IsBit, int Index)>();
        foreach (ScannedPoint point in points)
        {
            for (int i = 0; i < point.CellWidth; i++)
                cells.Add((point.Address.Area, point.Address.IsBit, point.Address.Number + i));
        }

        var requests = new List<McRequest>();

        foreach (var group in cells
                     .GroupBy(cell => (cell.Area, cell.IsBit))
                     .OrderBy(g => (int)g.Key.Area)
                     .ThenBy(g => g.Key.IsBit))
        {
            int limit = group.Key.IsBit ? BitLimit : WordLimit;
            int[] indices = group.Select(cell => cell.Index).OrderBy(index => index).ToArray();

            int start = indices[0];
            int previous = indices[0];
            foreach (int index in indices.Skip(1))
            {
                if (index != previous + 1)
                {
                    Emit(requests, group.Key.Area, group.Key.IsBit, start, previous - start + 1, limit);
                    start = index;
                }

                previous = index;
            }

            Emit(requests, group.Key.Area, group.Key.IsBit, start, previous - start + 1, limit);
        }

        return requests;
    }

    private static void Emit(
        List<McRequest> requests,
        McArea area,
        bool isBit,
        int start,
        int length,
        int limit)
    {
        while (length > 0)
        {
            int take = Math.Min(length, limit);
            requests.Add(new McRequest(area, isBit, start, take));
            start += take;
            length -= take;
        }
    }
}
