namespace Frosty.ModSupport.Attributes;

public class HandlerAttribute : Attribute
{
    public int Hash { get; }
    
    public HandlerAttribute(int inHash)
    {
        Hash = inHash;
    }
}