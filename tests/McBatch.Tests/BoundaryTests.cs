using System.Net;
using System.Net.Sockets;
using HslCommunication.Profinet.Melsec;
using Xunit;

namespace McBatch.Tests;

/// <summary>
/// SPEC §7 边界条件 + §5 中标记为「单元测试」的 A4、A7。
/// 全部在扫描阶段(Plan)验证, 不依赖网络。
/// </summary>
public class BoundaryTests
{
    // A4 / §7 边界: 无任何标注属性的对象 → 0 请求, 不抛异常
    [Fact]
    public void A4_NoAnnotatedProperties_ZeroRequestsWithoutThrowing()
    {
        var model = new EmptyModel { Unmapped = 42 };

        IReadOnlyList<McRequest> plan = McBatchPlanner.Plan(model);

        Assert.Empty(plan);
        Assert.Equal(42, model.Unmapped);
        // SPEC §7 已修订: Read<T>() 对空对象返回 new T()(空实例)。
        // 本条仍通过 Plan 断言「0 请求」; 返回值语义按“只改签名、不动断言”的要求未新增断言。
    }

    // A4 / §7: Read<EmptyModel> 指向无监听端口(closed port)的 client
    // → 成功返回非空实例, 且各字段为 CLR 默认值。
    // 同时钉死两件事: ①空对象返回 new T() ②0 请求时不触碰网络
    // (若实现尝试连接/读取, 到 closed port 会失败或抛异常, 本测试即失败)。
    [Fact]
    public void A4_EmptyModel_ReadReturnsDefaultInstance_WithoutTouchingNetwork()
    {
        int closedPort = GetClosedTcpPort();
        using var client = new MelsecMcNet("127.0.0.1", closedPort);

        EmptyModel model = McBatchReader.Read<EmptyModel>(client);

        Assert.NotNull(model);
        Assert.Equal(0, model.Unmapped);
    }

    // §7 边界: 两个属性标注同一地址 → 允许, 合并为一次覆盖, 不抛异常
    [Fact]
    public void DuplicateAddress_IsAllowed_AndCoveredExactlyOnce()
    {
        IReadOnlyList<McRequest> plan = McBatchPlanner.Plan(new DuplicateAddressModel());

        McRequest request = Assert.Single(plan);
        Assert.Equal(McArea.D, request.Area);
        Assert.False(request.IsBit);
        Assert.Equal(300, request.Start);
        Assert.Equal(1, request.Count);
        // "各自独立读回"需要仿真服务器, 见
        // ConformanceTests.DuplicateAddress_BothPropertiesReadBackIndependently。
    }

    // A7 / §7 边界: 非法地址(空串/未知区域/负数/格式错) → 扫描阶段抛 ArgumentException
    [Theory]
    [InlineData("empty")]
    [InlineData("no-digits")]
    [InlineData("negative")]
    [InlineData("fraction")]
    [InlineData("trailing")]
    [InlineData("unknown-area")]
    public void A7_InvalidAddress_ThrowsArgumentExceptionAtScan(string caseName)
    {
        object model = caseName switch
        {
            "empty" => new EmptyAddressModel(),
            "no-digits" => new NoDigitsAddressModel(),
            "negative" => new NegativeAddressModel(),
            "fraction" => new FractionAddressModel(),
            "trailing" => new TrailingGarbageAddressModel(),
            "unknown-area" => new UnknownAreaModel(),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, "未定义的用例"),
        };

        // 允许 ArgumentException 的派生类型(如 ArgumentOutOfRangeException),
        // 但 FormatException/OverflowException 等非 ArgumentException 不算达标。
        Assert.ThrowsAny<ArgumentException>(() => McBatchPlanner.Plan(model));
    }

    // A7 / §7 边界: 类型与区域不匹配 / 不支持的 CLR 类型 → 扫描阶段抛 ArgumentException
    // (bool 标在 D 区; 字类型标在位区; string/double 不在 §2.2 类型表内)
    [Theory]
    [InlineData("bool-on-D")]
    [InlineData("word-on-M")]
    [InlineData("string-on-D")]
    [InlineData("double-on-D")]
    public void A7_TypeAreaMismatchOrUnsupportedType_ThrowsArgumentExceptionAtScan(string caseName)
    {
        object model = caseName switch
        {
            "bool-on-D" => new BoolOnDModel(),
            "word-on-M" => new ShortOnMModel(),
            "string-on-D" => new StringOnDModel(),
            "double-on-D" => new DoubleOnDModel(),
            _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, "未定义的用例"),
        };

        // 允许 ArgumentException 的派生类型(如 ArgumentOutOfRangeException),
        // 但 FormatException/OverflowException 等非 ArgumentException 不算达标。
        Assert.ThrowsAny<ArgumentException>(() => McBatchPlanner.Plan(model));
    }

    // §2.3 统一解析入口: D 区为十进制字元件
    [Fact]
    public void McAddress_Parse_D_IsDecimalWord()
    {
        McAddress address = McAddress.Parse("D100");

        Assert.Equal(McArea.D, address.Area);
        Assert.Equal(100, address.Number);
        Assert.False(address.IsBit);
    }

    // §2.3 统一解析入口: M 区为十进制位元件
    [Fact]
    public void McAddress_Parse_M_IsDecimalBit()
    {
        McAddress address = McAddress.Parse("M10");

        Assert.Equal(McArea.M, address.Area);
        Assert.Equal(10, address.Number);
        Assert.True(address.IsBit);
    }

    // §2.3 ⚠️ 已知未知项 #1: X/Y 进制未钉死
    // 本测试只断言「区域/单位」, 刻意不断言 Number;
    // 进制结论以 ConformanceTests.A3_Xy_Addressing_RoundTrip 的实测
    // 加人工核对为准, 由用户回填 SPEC §2.3。
    [Theory]
    [InlineData("X10", "X")]
    [InlineData("Y10", "Y")]
    public void McAddress_Parse_Xy_UnitKnown_RadixPendingHumanConfirmation(string text, string expectedArea)
    {
        McAddress address = McAddress.Parse(text);

        Assert.Equal(expectedArea, address.Area.ToString());
        Assert.True(address.IsBit);
    }

    // 取一个刚释放的回环端口: 当前无任何进程监听, 连接会立刻被拒绝。
    private static int GetClosedTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // ---------- 测试模型 ----------

    private sealed class EmptyModel
    {
        public int Unmapped { get; set; }
    }

    private sealed class DuplicateAddressModel
    {
        [McPoint("D300")] public short First { get; set; }
        [McPoint("D300")] public short Second { get; set; }
    }

    private sealed class EmptyAddressModel
    {
        [McPoint("")] public short Value { get; set; }
    }

    private sealed class NoDigitsAddressModel
    {
        [McPoint("D")] public short Value { get; set; }
    }

    private sealed class NegativeAddressModel
    {
        [McPoint("D-1")] public short Value { get; set; }
    }

    private sealed class FractionAddressModel
    {
        [McPoint("D1.5")] public short Value { get; set; }
    }

    private sealed class TrailingGarbageAddressModel
    {
        [McPoint("D100x")] public short Value { get; set; }
    }

    private sealed class UnknownAreaModel
    {
        [McPoint("Z100")] public short Value { get; set; }
    }

    private sealed class BoolOnDModel
    {
        [McPoint("D100")] public bool Value { get; set; }
    }

    private sealed class ShortOnMModel
    {
        [McPoint("M10")] public short Value { get; set; }
    }

    private sealed class StringOnDModel
    {
        [McPoint("D100")] public string Value { get; set; } = string.Empty;
    }

    private sealed class DoubleOnDModel
    {
        [McPoint("D100")] public double Value { get; set; }
    }
}
