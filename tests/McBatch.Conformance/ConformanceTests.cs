using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using HslCommunication;
using HslCommunication.Profinet.Melsec;
using McBatch.TestSupport;
using Xunit;

namespace McBatch.Conformance;

/// <summary>
/// 基于 HslCommunication 的 MelsecMcServer 仿真服务器做端到端验收:
/// A1(实测往返数)、A3(X/Y round-trip)、A5(逐字段回读)、A6(100 点位耗时),
/// 以及 §7 的「同一地址两属性各自独立读回」。
/// </summary>
public sealed class ConformanceTests : IClassFixture<MelsecServerFixture>, IDisposable
{
    private readonly MelsecServerFixture _fixture;
    private readonly MelsecMcNet _client;

    public ConformanceTests(MelsecServerFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Connect();
    }

    public void Dispose() => _client.Dispose();

    // A1: 7 点位对象(含 2 个连续 D 点) → 实测往返数 ≤ 6
    [Fact]
    public void A1_SevenPointsWithConsecutiveD_RoundTripsAtMostSix()
    {
        using var counting = new RoundTripCountingMcNet("127.0.0.1", _fixture.Port);
        OperateResult connect = counting.ConnectServer();
        Assert.True(connect.IsSuccess, $"连接 MelsecMcServer 失败: {connect.Message}");
        counting.ResetCounters();

        var model = McBatchReader.Read<SevenPointModel>(counting);

        Assert.True(counting.RoundTrips <= 6,
            $"A1: 7 点位实测往返数 {counting.RoundTrips}, 超过上限 6。\n" +
            $"请求计划: {DescribePlan(model)}");
    }

    // A3: X/Y 寻址 round-trip 一致(写入已知值 → 批量读回逐字段相等)
    // 选用 X8/X10、X17/X15(八进制 17)、X70(八进制 70=56) 等对进制敏感的地址:
    // 若实现把 X/Y 当八进制或十六进制, 将读到与写入不同的单元而失败。
    // TODO(⚠️ 已知未知项 #1): 本测试只证明「客户端写入地址」与「实现解析地址」在
    // MelsecMcServer 上是同一单元; 进制(十进制/八进制/十六进制)与换算公式需人工
    // 实测核对后回填 SPEC §2.3。实测环境: HslCommunication 13.0.3, MelsecMcServer(binary)。
    [Fact]
    public void A3_Xy_Addressing_RoundTrip()
    {
        WriteAll(
            ("X7", true), ("X8", false), ("X10", true), ("X17", false), ("X70", true),
            ("Y7", false), ("Y8", true), ("Y10", false), ("Y17", true), ("Y70", false));

        var model = McBatchReader.Read<XyModel>(_client);

        Assert.True(model.X7);
        Assert.False(model.X8);
        Assert.True(model.X10);
        Assert.False(model.X17);
        Assert.True(model.X70);
        Assert.False(model.Y7);
        Assert.True(model.Y8);
        Assert.False(model.Y10);
        Assert.True(model.Y17);
        Assert.False(model.Y70);
    }

    // A5: 对 MelsecMcServer 写入已知值 → 批量读回逐字段相等
    // 覆盖 short/ushort/int/uint/float(低地址字为低 16 位) 与 M 区 bool。
    [Fact]
    public void A5_KnownValues_ReadBackPerFieldEqual()
    {
        AssertSuccess(_client.Write("D200", (short)-1234), "write D200");
        AssertSuccess(_client.Write("D202", (ushort)65000), "write D202");
        AssertSuccess(_client.Write("D204", -123456789), "write D204");
        AssertSuccess(_client.Write("D206", 4000000000u), "write D206");
        AssertSuccess(_client.Write("D208", 3.25f), "write D208");
        AssertSuccess(_client.Write("M400", false), "write M400");
        AssertSuccess(_client.Write("M401", true), "write M401");

        var model = McBatchReader.Read<A5Model>(_client);

        Assert.Equal((short)-1234, model.SignedWord);
        Assert.Equal((ushort)65000, model.UnsignedWord);
        Assert.Equal(-123456789, model.SignedDoubleWord);
        Assert.Equal(4000000000u, model.UnsignedDoubleWord);
        Assert.Equal(3.25f, model.Float);
        Assert.False(model.BitFalse);
        Assert.True(model.BitTrue);
    }

    // A6: 100 点位整对象读, 对本地 McServer < 50ms(预算可调:
    // 环境变量 MCBATCH_A6_BUDGET_MS, 未设置时 50)。
    // 计时取 5 次中的最小值, 并先预热 5 次以排除 JIT/反射初始化成本。
    [Fact]
    public void A6_HundredPointWholeObjectRead_UnderBudget()
    {
        int budgetMs = GetA6BudgetMs();
        Type modelType = DynamicModelBuilder.Create(BuildA6Points());
        // 契约只有泛型入口 T Read<T>(client), 动态生成的 100 点位类型需反射闭合。
        MethodInfo read = GetGenericRead().MakeGenericMethod(modelType);

        for (int i = 0; i < 5; i++)
            read.Invoke(null, new object[] { _client });

        double best = double.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            read.Invoke(null, new object[] { _client });
            stopwatch.Stop();
            best = Math.Min(best, stopwatch.Elapsed.TotalMilliseconds);
        }

        Assert.True(best < budgetMs,
            $"A6: 100 点位整对象读最快耗时 {best:F2}ms, 超过预算 {budgetMs}ms" +
            "(可用环境变量 MCBATCH_A6_BUDGET_MS 调整)。");
    }

    // §7 边界: 两个属性标注同一地址 → 允许, 各自独立读回
    [Fact]
    public void DuplicateAddress_BothPropertiesReadBackIndependently()
    {
        AssertSuccess(_client.Write("D500", (short)321), "write D500");

        var model = McBatchReader.Read<DuplicateAddressModel>(_client);

        Assert.Equal((short)321, model.First);
        Assert.Equal((short)321, model.Second);
        Assert.Single(McBatchPlanner.Plan(model));
    }

    // ---------- 辅助 ----------

    private void WriteAll(params (string Address, bool Value)[] writes)
    {
        foreach (var (address, value) in writes)
            AssertSuccess(_client.Write(address, value), $"write {address}");
    }

    private static void AssertSuccess(OperateResult result, string operation) =>
        Assert.True(result.IsSuccess, $"{operation} 失败: {result.Message}");

    private static MethodInfo GetGenericRead() =>
        typeof(McBatchReader)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(McBatchReader.Read)
                         && m.IsGenericMethodDefinition
                         && m.GetGenericArguments().Length == 1
                         && m.GetParameters().Length == 1);

    private static int GetA6BudgetMs()
    {
        string? raw = Environment.GetEnvironmentVariable("MCBATCH_A6_BUDGET_MS");
        if (string.IsNullOrWhiteSpace(raw))
            return 50;

        return int.TryParse(raw, out int value) && value > 0
            ? value
            : throw new InvalidOperationException(
                $"环境变量 MCBATCH_A6_BUDGET_MS 必须是正整数, 实际为 '{raw}'。");
    }

    private static List<PointSpec> BuildA6Points()
    {
        var points = new List<PointSpec>(100);
        for (int i = 0; i < 50; i++)
            points.Add(new PointSpec($"D{1000 + i}", typeof(short)));
        for (int i = 0; i < 30; i++)
            points.Add(new PointSpec($"M{1000 + i}", typeof(bool)));
        for (int i = 0; i < 10; i++)
            points.Add(new PointSpec($"X{300 + i}", typeof(bool)));
        for (int i = 0; i < 10; i++)
            points.Add(new PointSpec($"Y{300 + i}", typeof(bool)));
        return points;
    }

    private string DescribePlan(object model)
    {
        var plan = McBatchPlanner.Plan(model);
        return "[" + string.Join(", ",
            plan.Select(r => $"{r.Area}{(r.IsBit ? "B" : "W")}:{r.Start}+{r.Count}")) + "]";
    }

    // ---------- 测试模型 ----------

    private sealed class SevenPointModel
    {
        [McPoint("D100")] public short D100 { get; set; }
        [McPoint("D101")] public short D101 { get; set; }   // 与 D100 连续, 应合并
        [McPoint("D110")] public short D110 { get; set; }
        [McPoint("D120")] public float D120 { get; set; }   // 2 字
        [McPoint("M10")] public bool M10 { get; set; }
        [McPoint("M11")] public bool M11 { get; set; }      // 与 M10 连续, 应合并
        [McPoint("X5")] public bool X5 { get; set; }
    }

    private sealed class XyModel
    {
        [McPoint("X7")] public bool X7 { get; set; }
        [McPoint("X8")] public bool X8 { get; set; }
        [McPoint("X10")] public bool X10 { get; set; }
        [McPoint("X17")] public bool X17 { get; set; }
        [McPoint("X70")] public bool X70 { get; set; }
        [McPoint("Y7")] public bool Y7 { get; set; }
        [McPoint("Y8")] public bool Y8 { get; set; }
        [McPoint("Y10")] public bool Y10 { get; set; }
        [McPoint("Y17")] public bool Y17 { get; set; }
        [McPoint("Y70")] public bool Y70 { get; set; }
    }

    private sealed class A5Model
    {
        [McPoint("D200")] public short SignedWord { get; set; }
        [McPoint("D202")] public ushort UnsignedWord { get; set; }
        [McPoint("D204")] public int SignedDoubleWord { get; set; }
        [McPoint("D206")] public uint UnsignedDoubleWord { get; set; }
        [McPoint("D208")] public float Float { get; set; }
        [McPoint("M400")] public bool BitFalse { get; set; }
        [McPoint("M401")] public bool BitTrue { get; set; }
    }

    private sealed class DuplicateAddressModel
    {
        [McPoint("D500")] public short First { get; set; }
        [McPoint("D500")] public short Second { get; set; }
    }

    /// <summary>统计实际发出的 MC 读请求数; 只覆写长度重载, 避免 ReadBool(addr) 委派时重复计数。</summary>
    private sealed class RoundTripCountingMcNet : MelsecMcNet
    {
        private int _wordReads;
        private int _bitReads;

        public RoundTripCountingMcNet(string ipAddress, int port) : base(ipAddress, port)
        {
        }

        public int RoundTrips => _wordReads + _bitReads;

        public void ResetCounters()
        {
            _wordReads = 0;
            _bitReads = 0;
        }

        public override OperateResult<byte[]> Read(string address, ushort length)
        {
            _wordReads++;
            return base.Read(address, length);
        }

        public override OperateResult<bool[]> ReadBool(string address, ushort length)
        {
            _bitReads++;
            return base.ReadBool(address, length);
        }
    }
}

/// <summary>每个测试类共享一个已启动的 MelsecMcServer(二进制 3E 帧)。</summary>
public sealed class MelsecServerFixture : IDisposable
{
    private readonly MelsecMcServer _server;

    public int Port { get; }

    public MelsecServerFixture()
    {
        Port = GetFreeTcpPort();
        _server = new MelsecMcServer(isBinary: true);
        _server.ServerStart(Port);
        if (!_server.IsStarted)
            throw new InvalidOperationException($"MelsecMcServer 未能在端口 {Port} 启动。");
    }

    public MelsecMcNet Connect()
    {
        var client = new MelsecMcNet("127.0.0.1", Port);
        OperateResult result = client.ConnectServer();
        if (!result.IsSuccess)
        {
            client.Dispose();
            throw new InvalidOperationException($"连接 MelsecMcServer 失败: {result.Message}");
        }

        return client;
    }

    public void Dispose()
    {
        _server.ServerClose();
        _server.Dispose();
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
