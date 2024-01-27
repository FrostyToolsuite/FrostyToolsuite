using Frosty.Sdk.Attributes;
using Frosty.Sdk.Sdk;

namespace Frosty.Sdk.Ebx;

[EbxTypeMeta(TypeFlags.TypeEnum.Class)]
public partial struct PropertyConnection
{
    private string _Test;

    private uint _Flags;
}

public enum PropertyConnectionTargetType
{

}

public enum InputPropertyType
{

}