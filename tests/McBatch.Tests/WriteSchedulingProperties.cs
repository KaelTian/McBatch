using System.Globalization;
using McBatch.TestSupport;
using Xunit;
using Xunit.Sdk;

namespace McBatch.Tests;

/// <summary>
/// SPEC §8.3 A9: P1-P4 性质对写计划同样成立。
/// 与冻结的 SchedulingProperties 使用同一调度器(同一 BuildPlan), 但本文件:
///   1) 只调用写计划入口 McBatchPlanner.PlanForWrite(object);
///   2) 随机输入保证无重复地址、无地址单元重叠(SPEC §8.4);
///   3) oracle 在本文件内独立重推, 不引用冻结文件的私有实现。
/// </summary>
public class WriteSchedulingProperties
{
    // 与冻结的 SchedulingProperties 使用不同种子, 避免误以为在复用同一批输入。
    private const int Seed = 20261106;
    private const int Rounds = 200;
    private const int MaxPointsPerRound = 300;

    // SPEC §3.3 / §8.2: 字 ≤960 字/请求; 位 ≤7168 点/请求
    private const int WordLimit = 960;
    private const int BitLimit = 7168;

    private static readonly Type[] OneWordTypes = [typeof(short), typeof(ushort)];
    private static readonly Type[] TwoWordTypes = [typeof(int), typeof(uint), typeof(float)];
    private static readonly char[] AreaBag = ['D', 'D', 'D', 'D', 'D', 'D', 'M', 'M', 'M', 'X', 'Y'];

    // ---------- A9 / P1-P4 ----------

    // A9 / P1
    [Fact]
    public void WriteP1_Coverage_AllInputCellsCoveredExactly_200Rounds()
    {
        var rng = new Random(Seed);
        for (int round = 0; round < Rounds; round++)
        {
            var points = GenerateConflictFreePoints(rng);
            var plan = PlanForRound(points, round);

            var expected = ExpectedCells(points);
            var actual = new HashSet<(char Area, bool IsBit, int Index)>();
            foreach (var request in plan)
                foreach (var cell in CellsOf(request))
                    actual.Add(cell);

            var missing = expected.Except(actual).Take(10).ToList();
            var extra = actual.Except(expected).Take(10).ToList();
            Assert.True(missing.Count == 0 && extra.Count == 0,
                $"第 {round} 轮写计划 P1 失败(种子 {Seed})。\n" +
                $"缺失单元: {FormatCells(missing)}\n" +
                $"多余单元: {FormatCells(extra)}\n" +
                $"请求计划: {DescribePlan(plan)}\n" +
                $"输入:\n{Dump(points)}");
        }
    }

    // A9 / P2
    [Fact]
    public void WriteP2_NoOverlap_AnyTwoRequestsDisjoint_200Rounds()
    {
        var rng = new Random(Seed);
        for (int round = 0; round < Rounds; round++)
        {
            var points = GenerateConflictFreePoints(rng);
            var plan = PlanForRound(points, round);

            var owner = new Dictionary<(char Area, bool IsBit, int Index), int>();
            for (int index = 0; index < plan.Count; index++)
            {
                foreach (var cell in CellsOf(plan[index]))
                {
                    if (owner.TryGetValue(cell, out int firstIndex))
                    {
                        Assert.Fail(
                            $"第 {round} 轮写计划 P2 失败(种子 {Seed}): 单元 {FormatCell(cell)} " +
                            $"同时被请求 #{firstIndex} 与 #{index} 覆盖。\n" +
                            $"请求计划: {DescribePlan(plan)}\n" +
                            $"输入:\n{Dump(points)}");
                    }
                    owner[cell] = index;
                }
            }
        }
    }

    // A9 / P3
    [Fact]
    public void WriteP3_ProtocolLimits_AndUnitConsistency_200Rounds()
    {
        var rng = new Random(Seed);
        for (int round = 0; round < Rounds; round++)
        {
            var points = GenerateConflictFreePoints(rng);
            var plan = PlanForRound(points, round);

            for (int index = 0; index < plan.Count; index++)
            {
                var request = plan[index];
                bool expectBit = request.Area != McArea.D;
                int limit = request.IsBit ? BitLimit : WordLimit;
                Assert.True(request.Count >= 1,
                    $"第 {round} 轮写计划 P3 失败: 请求 #{index} Count={request.Count} 必须 ≥ 1。\n{DescribePlan(plan)}\n输入:\n{Dump(points)}");
                Assert.True(request.Start >= 0,
                    $"第 {round} 轮写计划 P3 失败: 请求 #{index} Start={request.Start} 必须 ≥ 0。\n{DescribePlan(plan)}\n输入:\n{Dump(points)}");
                Assert.True(request.IsBit == expectBit,
                    $"第 {round} 轮写计划 P3 失败: 区域 {request.Area} 的 IsBit 应为 {expectBit}, 实际 {request.IsBit}。\n{DescribePlan(plan)}\n输入:\n{Dump(points)}");
                Assert.True(request.Count <= limit,
                    $"第 {round} 轮写计划 P3 失败: 请求 #{index} 覆盖 {request.Count} 个{(request.IsBit ? "点" : "字")}, 超过上限 {limit}。\n{DescribePlan(plan)}\n输入:\n{Dump(points)}");
            }
        }
    }

    // A9 / P4
    [Fact]
    public void WriteP4_MinimalPartition_200Rounds()
    {
        var rng = new Random(Seed);
        for (int round = 0; round < Rounds; round++)
        {
            var points = GenerateConflictFreePoints(rng);
            var plan = PlanForRound(points, round);

            string expected = Canonical(ExpectedRanges(points));
            string actual = Canonical(plan.Select(r => (AreaChar(r.Area), r.IsBit, r.Start, r.Count)));
            Assert.True(expected == actual,
                $"第 {round} 轮写计划 P4 失败(种子 {Seed}): 请求分区不是理论最小值。\n" +
                $"期望: {expected}\n" +
                $"实际: {actual}\n" +
                $"输入:\n{Dump(points)}");
        }
    }

    // A9 / P3 / P4: 字元件 960 字上限的合并与拆包边界
    [Fact]
    public void WriteP3_P4_WordChunkLimit_960_961_1920()
    {
        var exact = PlanForConsecutive('D', 960, typeof(short));
        Assert.Equal("DW:0+960", Canonical(exact.Select(r => (AreaChar(r.Area), r.IsBit, r.Start, r.Count))));

        var overByOne = PlanForConsecutive('D', 961, typeof(short));
        Assert.Equal("DW:0+960;DW:960+1", Canonical(overByOne.Select(r => (AreaChar(r.Area), r.IsBit, r.Start, r.Count))));

        var twoFull = PlanForConsecutive('D', 1920, typeof(short));
        Assert.Equal("DW:0+960;DW:960+960", Canonical(twoFull.Select(r => (AreaChar(r.Area), r.IsBit, r.Start, r.Count))));
    }

    // A9 / P3 / P4: 位元件 7168 点上限的合并与拆包边界
    [Fact]
    public void WriteP3_P4_BitChunkLimit_7168_7169()
    {
        var exact = PlanForConsecutive('M', 7168, typeof(bool));
        Assert.Equal("MB:0+7168", Canonical(exact.Select(r => (AreaChar(r.Area), r.IsBit, r.Start, r.Count))));

        var overByOne = PlanForConsecutive('M', 7169, typeof(bool));
        Assert.Equal("MB:0+7168;MB:7168+1", Canonical(overByOne.Select(r => (AreaChar(r.Area), r.IsBit, r.Start, r.Count))));
    }

    // ---------- 随机输入生成(固定种子, 无地址冲突) ----------

    /// <summary>
    /// 生成写场景合法的随机点位: 任意两个属性覆盖的地址单元不得相交
    /// (SPEC §8.4: 同一地址、规范化后相同、多字点位重叠均不允许)。
    /// </summary>
    private static List<PointSpec> GenerateConflictFreePoints(Random rng)
    {
        int count = rng.Next(0, MaxPointsPerRound + 1);
        var points = new List<PointSpec>(count);
        var occupied = new HashSet<(char Area, bool IsBit, int Index)>();
        int attempts = 0;

        while (points.Count < count)
        {
            if (++attempts > 100_000)
                throw new InvalidOperationException("无法生成无冲突点位(生成器 bug)。");

            char area = AreaBag[rng.Next(AreaBag.Length)];
            int number;
            if (points.Count > 0 && rng.Next(2) == 0)
            {
                // 约一半点位围绕已有地址生成, 制造相邻/连续段以压测合并逻辑。
                var near = points[rng.Next(points.Count)];
                int nearNumber = AddressNumber(near.Address);
                number = Math.Max(0, nearNumber + rng.Next(-3, 8));
            }
            else
            {
                number = rng.Next(0, MaxAddress(area));
            }

            Type clrType = area == 'D'
                ? (rng.Next(10) < 7
                    ? OneWordTypes[rng.Next(OneWordTypes.Length)]
                    : TwoWordTypes[rng.Next(TwoWordTypes.Length)])
                : typeof(bool);

            bool isBit = area != 'D';
            int width = isBit ? 1 : WordWidth(clrType);
            if (number + width > MaxAddress(area))
                continue;

            bool conflict = false;
            for (int i = 0; i < width; i++)
            {
                if (occupied.Contains((area, isBit, number + i)))
                {
                    conflict = true;
                    break;
                }
            }
            if (conflict)
                continue;

            for (int i = 0; i < width; i++)
                occupied.Add((area, isBit, number + i));

            points.Add(new PointSpec(
                area.ToString(CultureInfo.InvariantCulture) + number.ToString(CultureInfo.InvariantCulture),
                clrType));
        }

        return points;
    }

    private static int MaxAddress(char area) => area switch
    {
        'D' => 5000,
        'M' => 3000,
        'X' => 2000,
        'Y' => 2000,
        _ => throw new InvalidOperationException($"未知区域 {area}"),
    };

    private static IReadOnlyList<McRequest> PlanForRound(IReadOnlyList<PointSpec> points, int round)
    {
        object target = DynamicModelBuilder.CreateInstance(points);
        try
        {
            return McBatchPlanner.PlanForWrite(target);
        }
        catch (Exception ex)
        {
            throw new XunitException(
                $"第 {round} 轮 PlanForWrite() 抛出 {ex.GetType().Name}: {ex.Message}\n输入:\n{Dump(points)}");
        }
    }

    private static IReadOnlyList<McRequest> PlanForConsecutive(char area, int count, Type clrType)
    {
        var points = new List<PointSpec>(count);
        for (int i = 0; i < count; i++)
        {
            points.Add(new PointSpec(
                area.ToString(CultureInfo.InvariantCulture) + i.ToString(CultureInfo.InvariantCulture),
                clrType));
        }

        return McBatchPlanner.PlanForWrite(DynamicModelBuilder.CreateInstance(points));
    }

    // ---------- 独立 oracle(不调用被测解析器) ----------

    private static HashSet<(char Area, bool IsBit, int Index)> ExpectedCells(IReadOnlyList<PointSpec> points)
    {
        var cells = new HashSet<(char Area, bool IsBit, int Index)>();
        foreach (var point in points)
        {
            char area = point.Address[0];
            bool isBit = area != 'D';
            int number = AddressNumber(point.Address);
            int width = isBit ? 1 : WordWidth(point.ClrType);
            for (int i = 0; i < width; i++)
                cells.Add((area, isBit, number + i));
        }

        return cells;
    }

    private static List<(char Area, bool IsBit, int Start, int Count)> ExpectedRanges(IReadOnlyList<PointSpec> points)
    {
        var result = new List<(char Area, bool IsBit, int Start, int Count)>();
        foreach (var group in ExpectedCells(points).GroupBy(c => (c.Area, c.IsBit)))
        {
            var indices = group.Select(c => c.Index).OrderBy(i => i).ToList();
            int limit = group.Key.IsBit ? BitLimit : WordLimit;
            int start = indices[0];
            int previous = indices[0];
            foreach (int current in indices.Skip(1))
            {
                if (current != previous + 1)
                {
                    Split(result, group.Key.Area, group.Key.IsBit, start, previous - start + 1, limit);
                    start = current;
                }
                previous = current;
            }
            Split(result, group.Key.Area, group.Key.IsBit, start, previous - start + 1, limit);
        }

        return result;
    }

    private static void Split(
        List<(char Area, bool IsBit, int Start, int Count)> output,
        char area,
        bool isBit,
        int start,
        int length,
        int limit)
    {
        while (length > 0)
        {
            int take = Math.Min(length, limit);
            output.Add((area, isBit, start, take));
            start += take;
            length -= take;
        }
    }

    private static int WordWidth(Type clrType) =>
        clrType == typeof(short) || clrType == typeof(ushort) ? 1 : 2;

    private static int AddressNumber(string address) =>
        int.Parse(address.AsSpan(1), CultureInfo.InvariantCulture);

    private static IEnumerable<(char Area, bool IsBit, int Index)> CellsOf(McRequest request)
    {
        char area = AreaChar(request.Area);
        for (int i = 0; i < request.Count; i++)
            yield return (area, request.IsBit, request.Start + i);
    }

    private static char AreaChar(McArea area) => area.ToString()[0];

    // ---------- 失败信息格式化 ----------

    private static string Canonical(IEnumerable<(char Area, bool IsBit, int Start, int Count)> ranges) =>
        string.Join(";", ranges
            .Select(r => $"{r.Area}{(r.IsBit ? "B" : "W")}:{r.Start}+{r.Count}")
            .OrderBy(s => s, StringComparer.Ordinal));

    private static string DescribePlan(IEnumerable<McRequest> plan) =>
        "[" + string.Join(", ", plan.Select(r =>
            $"{AreaChar(r.Area)}{(r.IsBit ? "B" : "W")}:{r.Start}+{r.Count}")) + "]";

    private static string FormatCells(IReadOnlyList<(char Area, bool IsBit, int Index)> cells) =>
        cells.Count == 0 ? "(无)" : string.Join(", ", cells.Select(FormatCell));

    private static string FormatCell((char Area, bool IsBit, int Index) cell) =>
        $"{cell.Area}{(cell.IsBit ? "B" : "W")}{cell.Index}";

    private static string Dump(IReadOnlyList<PointSpec> points) =>
        points.Count == 0
            ? "  (0 个点位)"
            : string.Join("\n", points.Select((p, i) => $"  [{i}] {p.Address} : {p.ClrType.Name}"));
}
