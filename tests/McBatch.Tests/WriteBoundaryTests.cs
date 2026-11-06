using System.Net;
using System.Net.Sockets;
using HslCommunication;
using HslCommunication.Profinet.Melsec;
using Xunit;

namespace McBatch.Tests;

/// <summary>
/// SPEC §8.3 A10/A11 的单元测试: 空对象写不触网; 写场景地址冲突在扫描阶段失败。
/// 全部通过写计划入口或计数客户端验证, 不依赖真实 PLC。
/// </summary>
public class WriteBoundaryTests
{
    // A10
    // "0 请求"由本测试通过写计划入口断言; "不触网"见下一条测试。
    [Fact]
    public void A10_EmptyModel_WritePlanIsEmpty()
    {
        IReadOnlyList<McRequest> plan = McBatchPlanner.PlanForWrite(new EmptyModel());

        Assert.Empty(plan);
    }

    // A10
    // 观测方案: 使用自定义计数客户端 NetworkCountingMcNet, 指向一个刚释放的回环端口
    // (无效端口: 没有任何进程监听)。它覆写 ConnectServer / Read / ReadBool /
    // Write(byte[]) / Write(bool[]) 并分别计数。
    // 空对象 Write 返回后断言 TotalNetworkCalls == 0:
    //   - 任何一次连接、读或写都会使对应计数 > 0, 测试失败;
    //   - 计数为 0 即证明“零网络往返”——不仅没有成功往返, 连一次网络调用/连接尝试都没有。
    // 不采用"仅靠无效端口抛异常"的写法: 连接失败无法区分"尝试过但失败"与"从未尝试"。
    // 也不采用计数 server: 若实现错误地建立连接并等待响应, 测试会挂起而不是快速失败。
    [Fact]
    public void A10_EmptyModel_WriteToInvalidPort_ZeroNetworkCalls()
    {
        int closedPort = GetClosedTcpPort();
        using var client = new NetworkCountingMcNet("127.0.0.1", closedPort);

        McBatchWriter.Write(client, new EmptyModel());

        Assert.Equal(0, client.ConnectCalls);
        Assert.Equal(0, client.ReadCalls);
        Assert.Equal(0, client.ReadBoolCalls);
        Assert.Equal(0, client.WriteByteCalls);
        Assert.Equal(0, client.WriteBoolCalls);
        Assert.Equal(0, client.TotalNetworkCalls);
    }

    // A11
    [Fact]
    public void A11_SameAddress_ThrowsArgumentExceptionAtScan()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => McBatchPlanner.PlanForWrite(new DuplicateAddressModel()));
    }

    // A11
    // SPEC §8.4(c): 地址规范化后相同(D100 与 D0100)视为同一地址。
    [Fact]
    public void A11_EquivalentStrings_ThrowsArgumentExceptionAtScan()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => McBatchPlanner.PlanForWrite(new EquivalentAddressModel()));
    }

    // A11
    // SPEC §8.4(b): 多字点位造成的地址单元重叠(int D100 覆盖 D100-D101, short D101)。
    [Fact]
    public void A11_OverlappingFootprint_ThrowsArgumentExceptionAtScan()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => McBatchPlanner.PlanForWrite(new OverlappingFootprintModel()));
    }

    // A11
    // 位写入路径同样必须判重, 防止校验只覆盖字类型。
    [Fact]
    public void A11_DuplicateBitAddress_ThrowsArgumentExceptionAtScan()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => McBatchPlanner.PlanForWrite(new DuplicateBitModel()));
    }

    // A11
    // 观测方案: 用同一个计数客户端指向无效端口; 若 Write 在扫描阶段抛
    // ArgumentException 且所有网络计数仍为 0, 即证明写路径没有绕过校验、
    // 且校验发生在任何网络调用之前。
    [Fact]
    public void A11_WritePath_ValidatesBeforeNetwork()
    {
        int closedPort = GetClosedTcpPort();
        using var client = new NetworkCountingMcNet("127.0.0.1", closedPort);

        Assert.ThrowsAny<ArgumentException>(
            () => McBatchWriter.Write(client, new DuplicateAddressModel()));
        Assert.Equal(0, client.TotalNetworkCalls);
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

    /// <summary>
    /// 统计所有可能触网的调用次数。指向无效端口时,
    /// 任何一次计数 > 0 都说明实现尝试了网络操作。
    /// </summary>
    private sealed class NetworkCountingMcNet : MelsecMcNet
    {
        public NetworkCountingMcNet(string ipAddress, int port) : base(ipAddress, port)
        {
        }

        public int ConnectCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public int ReadBoolCalls { get; private set; }
        public int WriteByteCalls { get; private set; }
        public int WriteBoolCalls { get; private set; }

        public int TotalNetworkCalls =>
            ConnectCalls + ReadCalls + ReadBoolCalls + WriteByteCalls + WriteBoolCalls;

        public override OperateResult ConnectServer()
        {
            ConnectCalls++;
            return base.ConnectServer();
        }

        public override OperateResult<byte[]> Read(string address, ushort length)
        {
            ReadCalls++;
            return base.Read(address, length);
        }

        public override OperateResult<bool[]> ReadBool(string address, ushort length)
        {
            ReadBoolCalls++;
            return base.ReadBool(address, length);
        }

        public override OperateResult Write(string address, byte[] value)
        {
            WriteByteCalls++;
            return base.Write(address, value);
        }

        public override OperateResult Write(string address, bool[] value)
        {
            WriteBoolCalls++;
            return base.Write(address, value);
        }
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

    private sealed class EquivalentAddressModel
    {
        [McPoint("D100")] public short First { get; set; }
        [McPoint("D0100")] public short Second { get; set; }
    }

    private sealed class OverlappingFootprintModel
    {
        [McPoint("D100")] public int Wide { get; set; }      // 覆盖 D100-D101
        [McPoint("D101")] public short Narrow { get; set; }  // 与 Wide 重叠 D101
    }

    private sealed class DuplicateBitModel
    {
        [McPoint("M10")] public bool First { get; set; }
        [McPoint("M10")] public bool Second { get; set; }
    }
}
