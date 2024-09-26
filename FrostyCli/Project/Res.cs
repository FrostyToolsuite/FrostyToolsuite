using Frosty.Sdk.Managers.Entries;

namespace FrostyCli.Project;

public class Res : Asset
{
    public ResourceType ResType { get; set; }
    public ulong ResRid { get; set; }
}