namespace Frosty.Sdk.Interfaces;

public interface IPrimitive
{
    public object ToActualType();
    public void FromActualType(object value);
}