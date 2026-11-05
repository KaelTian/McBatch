using System.Globalization;

namespace McBatch;

/// <summary>解析后的点位地址。全项目唯一的地址解析入口。</summary>
public readonly struct McAddress
{
    private McAddress(McArea area, int number)
    {
        Area = area;
        Number = number;
    }

    public McArea Area { get; }

    /// <summary>区域内的十进制编号(D/M/X/Y 均按十进制解析)。</summary>
    public int Number { get; }

    public bool IsBit => Area != McArea.D;

    public static McAddress Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("点位地址不能为空。", nameof(text));

        McArea area = text[0] switch
        {
            'D' => McArea.D,
            'M' => McArea.M,
            'X' => McArea.X,
            'Y' => McArea.Y,
            _ => throw new ArgumentException($"未知的区域前缀 '{text[0]}', 仅支持 D/M/X/Y。", nameof(text)),
        };

        // NumberStyles.None 拒绝符号、空白、小数点和尾随字符, 溢出同样返回 false。
        if (!int.TryParse(text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
            throw new ArgumentException($"点位地址 '{text}' 的编号格式非法, 必须是非负十进制整数。", nameof(text));

        return new McAddress(area, number);
    }
}
