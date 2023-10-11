using Frosty.Sdk.Attributes;
using Frosty.Sdk.Sdk;

namespace Frosty.Sdk.Ebx;

[EbxTypeMeta(TypeFlags.TypeEnum.Class)]
public partial class DataContainer
{
}

public partial class Asset : DataContainer
{
    [Attributes.EbxFieldMeta(Frosty.Sdk.Sdk.TypeFlags.TypeEnum.CString, 0u)]
    private CString _Name;
}

public partial class DataContainerAsset : DataContainer
{
    private CString _Name;
}