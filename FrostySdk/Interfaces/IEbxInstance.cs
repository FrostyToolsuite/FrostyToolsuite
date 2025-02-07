namespace Frosty.Sdk.Interfaces;

public interface IEbxInstance
{
    public Ebx.AssetClassGuid GetInstanceGuid();

    public void SetInstanceGuid(Ebx.AssetClassGuid newGuid);
}