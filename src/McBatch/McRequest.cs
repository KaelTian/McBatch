namespace McBatch;

/// <summary>一个批量读请求: 覆盖同一区域内连续的一段字或位。</summary>
public sealed class McRequest
{
    internal McRequest(McArea area, bool isBit, int start, int count)
    {
        Area = area;
        IsBit = isBit;
        Start = start;
        Count = count;
    }

    public McArea Area { get; }

    public bool IsBit { get; }

    public int Start { get; }

    /// <summary>请求覆盖的单元数: 字请求为字数, 位请求为点数。</summary>
    public int Count { get; }
}
