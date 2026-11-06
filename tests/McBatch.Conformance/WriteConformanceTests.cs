using HslCommunication.Profinet.Melsec;
using Xunit;

namespace McBatch.Conformance;

/// <summary>
/// SPEC §8.3 A8: 写入已知值 → 立即用 Read&lt;T&gt; 批量读回 → 逐字段相等。
/// 复用 ConformanceTests.cs 中已定义的公共 MelsecServerFixture(仅类型引用, 不修改冻结文件)。
/// </summary>
public sealed class WriteConformanceTests : IClassFixture<MelsecServerFixture>, IDisposable
{
    private readonly MelsecMcNet _client;

    public WriteConformanceTests(MelsecServerFixture fixture)
    {
        _client = fixture.Connect();
    }

    public void Dispose() => _client.Dispose();

    // A8
    // 覆盖 short/ushort/int/uint/float(低地址字为低 16 位)与 M 区 bool。
    // TODO(⚠️ Q8): A8 是否包含 X/Y 未裁决; X/Y 进制见 SPEC §2.3 ⚠️ #1, 本测试暂不覆盖。
    [Fact]
    public void A8_MixedTypeModel_WriteThenRead_FieldsEqual()
    {
        var original = new MixedModel
        {
            SignedWord = -1234,
            UnsignedWord = 65000,
            SignedDoubleWord = -123456789,
            UnsignedDoubleWord = 4000000000u,
            Float = 3.25f,
            BitFalse = false,
            BitTrue = true,
        };

        McBatchWriter.Write(_client, original);
        MixedModel read = McBatchReader.Read<MixedModel>(_client);

        Assert.Equal(original.SignedWord, read.SignedWord);
        Assert.Equal(original.UnsignedWord, read.UnsignedWord);
        Assert.Equal(original.SignedDoubleWord, read.SignedDoubleWord);
        Assert.Equal(original.UnsignedDoubleWord, read.UnsignedDoubleWord);
        Assert.Equal(original.Float, read.Float);
        Assert.Equal(original.BitFalse, read.BitFalse);
        Assert.Equal(original.BitTrue, read.BitTrue);
    }

    // A8
    // 16 个连续位(M160-M175, 起始地址 16 对齐)的打包写入 round-trip。
    // TODO(⚠️ Q1): 位打包的执行方式(库位单位写 vs 手工拼字)未裁决;
    // 本测试只断言 round-trip, 不假设具体报文格式, 也不单独证明 b0=最低地址的线序。
    [Fact]
    public void A8_SixteenBitPattern_WriteThenRead_FieldsEqual()
    {
        var original = new SixteenBitModel
        {
            B0 = true,
            B1 = false,
            B2 = true,
            B3 = false,
            B4 = true,
            B5 = false,
            B6 = true,
            B7 = false,
            B8 = false,
            B9 = true,
            B10 = false,
            B11 = true,
            B12 = false,
            B13 = true,
            B14 = false,
            B15 = true,
        };

        McBatchWriter.Write(_client, original);
        SixteenBitModel read = McBatchReader.Read<SixteenBitModel>(_client);

        Assert.Equal(original.B0, read.B0);
        Assert.Equal(original.B1, read.B1);
        Assert.Equal(original.B2, read.B2);
        Assert.Equal(original.B3, read.B3);
        Assert.Equal(original.B4, read.B4);
        Assert.Equal(original.B5, read.B5);
        Assert.Equal(original.B6, read.B6);
        Assert.Equal(original.B7, read.B7);
        Assert.Equal(original.B8, read.B8);
        Assert.Equal(original.B9, read.B9);
        Assert.Equal(original.B10, read.B10);
        Assert.Equal(original.B11, read.B11);
        Assert.Equal(original.B12, read.B12);
        Assert.Equal(original.B13, read.B13);
        Assert.Equal(original.B14, read.B14);
        Assert.Equal(original.B15, read.B15);
    }

    // A8(待裁决)
    // TODO(⚠️ Q9): 非 16 对齐起始位(如 M3..M18)的写行为未实测/未裁决。
    // 在 SPEC 明确对齐/尾部语义前, 不写死期望值。
    [Fact(Skip = "TODO(⚠️ Q9): 非 16 对齐起始位行为待裁决")]
    public void A8_BitWrite_NonAlignedStart_RoundTrip_PendingDecision()
    {
    }

    // A8(待裁决)
    // TODO(⚠️ Q10): 同一 16 位字内未被寻址的相邻位是否必须保留未裁决。
    // 在 SPEC 明确前, 不写死"邻近位保持不变"的期望。
    [Fact(Skip = "TODO(⚠️ Q10): 邻近位保留语义待裁决")]
    public void A8_BitWrite_DoesNotClobberAdjacentBits_PendingDecision()
    {
    }

    // A8(待裁决)
    // TODO(⚠️ Q8): A8 是否覆盖 X/Y 未裁决; SPEC §2.3 ⚠️ #1 的 X/Y 进制未闭环。
    [Fact(Skip = "TODO(⚠️ Q8): X/Y 写入覆盖范围待裁决")]
    public void A8_XyWrite_RoundTrip_PendingDecision()
    {
    }

    // ---------- 测试模型 ----------

    private sealed class MixedModel
    {
        [McPoint("D200")] public short SignedWord { get; set; }
        [McPoint("D202")] public ushort UnsignedWord { get; set; }
        [McPoint("D204")] public int SignedDoubleWord { get; set; }
        [McPoint("D206")] public uint UnsignedDoubleWord { get; set; }
        [McPoint("D208")] public float Float { get; set; }
        [McPoint("M400")] public bool BitFalse { get; set; }
        [McPoint("M401")] public bool BitTrue { get; set; }
    }

    private sealed class SixteenBitModel
    {
        [McPoint("M160")] public bool B0 { get; set; }
        [McPoint("M161")] public bool B1 { get; set; }
        [McPoint("M162")] public bool B2 { get; set; }
        [McPoint("M163")] public bool B3 { get; set; }
        [McPoint("M164")] public bool B4 { get; set; }
        [McPoint("M165")] public bool B5 { get; set; }
        [McPoint("M166")] public bool B6 { get; set; }
        [McPoint("M167")] public bool B7 { get; set; }
        [McPoint("M168")] public bool B8 { get; set; }
        [McPoint("M169")] public bool B9 { get; set; }
        [McPoint("M170")] public bool B10 { get; set; }
        [McPoint("M171")] public bool B11 { get; set; }
        [McPoint("M172")] public bool B12 { get; set; }
        [McPoint("M173")] public bool B13 { get; set; }
        [McPoint("M174")] public bool B14 { get; set; }
        [McPoint("M175")] public bool B15 { get; set; }
    }
}
