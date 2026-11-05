namespace McBatch;

/// <summary>把属性标注为 PLC 点位; 地址形如 "D100" / "M10" / "X7" / "Y7"。</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class McPointAttribute : Attribute
{
    public McPointAttribute(string address)
    {
        Address = address;
    }

    public string Address { get; }
}
