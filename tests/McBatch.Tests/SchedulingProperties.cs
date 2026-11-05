using System.Globalization;
using McBatch.TestSupport;
using Xunit;
using Xunit.Sdk;

namespace McBatch.Tests;

/// <summary>
/// SPEC §4 验收性质 P1-P4 + §5 A2 的 property 测试。
/// 每轮随机生成 0~300 个点位, 用独立于实现的 oracle 计算期望覆盖/请求,
/// 失败时把「轮次 + 种子 + 完整输入」写进断言消息, 便于直接复现。
/// </summary>
public class SchedulingProperties
{
    // A2: 200 轮随机点位(0~300 个/轮) → P1-P3 全过
    private const int Seed = 20261105;
    private const int Rounds = 200;
    private const int MaxPointsPerRound = 300;

    // SPEC §3.3: 字 ≤960 字/请求; 位 ≤7168 点/请求
    private const int WordLimit = 960;
    private const int BitLimit = 7168;

    private static readonly Type[] OneWordTypes = [typeof(short), typeof(ushort)];
    private static readonly Type[] TwoWordTypes = [typeof(int), typeof(uint), typeof(float)];
    private static readonly char[] AreaBag = ['D', 'D', 'D', 'D', 'D', 'D', 'M', 'M', 'M', 'X', 'Y'];

    // P1 覆盖性: 所有请求覆盖的地址单元并集 == 输入点位集合(不丢、不多)
    // A2: 200 轮随机点位 → P1 全过
    [Fact]
    public void P1_Coverage_AllInputCellsCoveredExactly_200Rounds()
    {
        var rng = new Random(Seed);
        for (int round = 0; round < Rounds; round++)
        {
            var points = GeneratePoints(rng);
            var plan = PlanForRound(points, round);

            var expected = ExpectedCells(points);
            var actual = new HashSet<(char Area, bool IsBit, int Index)>();
            foreach (var request in plan)
                foreach (var cell in CellsOf(request))
                    actual.Add(cell);

            var missing = expected.Except(actual).Take(10).ToList();
            var extra = actual.Except(expected).Take(10).ToList();
            Assert.True(missing.Count == 0 && extra.Count == 0,
                $"第 {round} 轮 P1 失败(种子 {Seed})。\n" +
                $"缺失单元: {FormatCells(missing)}\n" +
                $"多余单元: {FormatCells(extra)}\n" +
                $"请求计划: {DescribePlan(plan)}\n" +
                $"输入:\n{Dump(points)}");
        }
    }

    // P2 无重叠: 任意两请求覆盖的地址单元不相交
    // A2: 200 轮随机点位 → P2 全过
    [Fact]
    public void P2_NoOverlap_AnyTwoRequestsDisjoint_200Rounds()
    {
        var rng = new Random(Seed);
        for (int round = 0; round < Rounds; round++)
        {
            var points = GeneratePoints(rng);
            var plan = PlanForRound(points, round);

            var owner = new Dictionary<(char Area, bool IsBit, int Index), int>();
            for (int index = 0; index < plan.Count; index++)
            {
                foreach (var cell in CellsOf(plan[index]))
                {
                    if (owner.TryGetValue(cell, out int firstIndex))
                    {
                        Assert.Fail(
                            $"第 {round} 轮 P2 失败(种子 {Seed}): 单元 {FormatCell(cell)} " +
                            $"同时被请求 #{firstIndex} 与 #{index} 覆盖。\n" +
                            $"请求计划: {DescribePlan(plan)}\n" +
                            $"输入:\n{Dump(points)}");
                    }
                    owner[cell] = index;
                }
            }
        }
    }

    // P3 合规性: 单批大小 ≤ 协议上限(字 960 / 位 7168), 且不得混入不同单位
    // A2: 200 轮随机点位 → P3 全过
    [Fact]
    public void P3_ProtocolLimits_AndUnitConsistency_200Rounds()
    {
        var rng = new Random(Seed);
        for (int round = 0; round < Rounds; round++)
        {
            var points = GeneratePoints(rng);
            var plan = PlanForRound(points, round);

            for (int index = 0; index < plan.Count; index++)
            {
                var request = plan[index];
                bool expectBit = request.Area != McArea.D;
                int limit = request.IsBit ? BitLimit : WordLimit;
                Assert.True(request.Count >= 1,
                    $"第 {round} 轮 P3 失败: 请求 #{index} Count={request.Count} 必须 ≥ 1。\n{DescribePlan(plan)}\n输入:\n{Dump(points)}");
                Assert.True(request.Start >= 0,
                    $"第 {round} 轮 P3 失败: 请求 #{index} Start={request.Start} 必须 ≥ 0。\n{DescribePlan(plan)}\n输入:\n{Dump(points)}");
                Assert.True(request.IsBit == expectBit,
                    $"第 {round} 轮 P3 失败: 区域 {request.Area} 的 IsBit 应为 {expectBit}, 实际 {request.IsBit}。\n{DescribePlan(plan)}\n输入:\n{Dump(points)}");
                Assert.True(request.Count <= limit,
                    $"第 {round} 轮 P3 失败: 请求 #{index} 覆盖 {request.Count} 个{(request.IsBit ? "点" : "字")}, 超过上限 {limit}。\n{DescribePlan(plan)}\n输入:\n{Dump(points)}");
            }
        }
    }

    // P4 最简性: 请求数 == 分组 + 合并 + 拆包后的理论最小值
    // (相邻段必须已合并; 超上限段必须已拆包)
    // A2: 200 轮随机点位 → 覆盖性/无重叠/合规性同时成立, 此处额外校验最简分区
    [Fact]
    public void P4_MinimalPartition_200Rounds()
    {
        var rng = new Random(Seed);
        for (int round = 0; round < Rounds; round++)
        {
            var points = GeneratePoints(rng);
            var plan = PlanForRound(points, round);

            string expected = Canonical(ExpectedRanges(points));
            string actual = Canonical(plan.Select(r => (AreaChar(r.Area), r.IsBit, r.Start, r.Count)));
            Assert.True(expected == actual,
                $"第 {round} 轮 P4 失败(种子 {Seed}): 请求分区不是理论最小值。\n" +
                $"期望: {expected}\n" +
                $"实际: {actual}\n" +
                $"输入:\n{Dump(points)}");
        }
    }

    // P3 合规性 / P4 最简性: 字元件 960 字上限的合并与拆包边界
    [Fact]
    public void P3_P4_WordChunkLimit_960_961_1920()
    {
        // 恰好 960 字 → 1 个请求, 不得多拆
        var exact = PlanForConsecutive('D', 960, typeof(short));
        Assert.Equal("DW:0+960", Canonical(exact.Select(r => (AreaChar(r.Area), r.IsBit, r.Start, r.Count))));

        // 961 字 → 960 + 1
        var overByOne = PlanForConsecutive('D', 961, typeof(short));
        Assert.Equal("DW:0+960;DW:960+1", Canonical(overByOne.Select(r => (AreaChar(r.Area), r.IsBit, r.Start, r.Count))));

        // 1920 字 → 960 + 960
        var twoFull = PlanForConsecutive('D', 1920, typeof(short));
        Assert.Equal("DW:0+960;DW:960+960", Canonical(twoFull.Select(r => (AreaChar(r.Area), r.IsBit, r.Start, r.Count))));
    }

    // P3 合规性 / P4 最简性: 位元件 7168 点上限的合并与拆包边界
    [Fact]
    public void P3_P4_BitChunkLimit_7168_7169()
    {
        // 恰好 7168 点 → 1 个请求
        var exact = PlanForConsecutive('M', 7168, typeof(bool));
        Assert.Equal("MB:0+7168", Canonical(exact.Select(r => (AreaChar(r.Area), r.IsBit, r.Start, r.Count))));

        // 7169 点 → 7168 + 1
        var overByOne = PlanForConsecutive('M', 7169, typeof(bool));
        Assert.Equal("MB:0+7168;MB:7168+1", Canonical(overByOne.Select(r => (AreaChar(r.Area), r.IsBit, r.Start, r.Count))));
    }

    // ---------- 随机输入生成(固定种子) ----------

    private static List<PointSpec> GeneratePoints(Random rng)
    {
        int count = rng.Next(0, MaxPointsPerRound + 1);
        var points = new List<PointSpec>(count);

        for (int i = 0; i < count; i++)
        {
            // 约 10% 的机会重复已有地址, 验证去重仍满足 P1/P2/P4
            if (points.Count > 0 && rng.Next(10) == 0)
            {
                points.Add(points[rng.Next(points.Count)]);
                continue;
            }

            char area = AreaBag[rng.Next(AreaBag.Length)];
            int number;
            if (points.Count > 0 && rng.Next(2) == 0)
            {
                // 约一半点位围绕已有地址生成, 制造相邻/连续段以压测合并逻辑
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
            return McBatchPlanner.Plan(target);
        }
        catch (Exception ex)
        {
            throw new XunitException(
                $"第 {round} 轮 Plan() 抛出 {ex.GetType().Name}: {ex.Message}\n输入:\n{Dump(points)}");
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

        return McBatchPlanner.Plan(DynamicModelBuilder.CreateInstance(points));
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
