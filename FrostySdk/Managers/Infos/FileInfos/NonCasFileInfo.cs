using Frosty.Sdk.Interfaces;
using Frosty.Sdk.IO;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.Managers.Infos.FileInfos;

public class NonCasFileInfo : IFileInfo
{
    public NonCasFileInfo(string inSuperBundleName, uint offset, uint size, uint logicalOffset = 0)
    {
        
    }
    
    public bool IsComplete()
    {
        throw new System.NotImplementedException();
    }

    public Block<byte> GetRawData()
    {
        throw new System.NotImplementedException();
    }

    public Block<byte> GetData(int inOriginalSize = 0)
    {
        throw new System.NotImplementedException();
    }

    public void SerializeInternal(DataStream stream)
    {
        throw new System.NotImplementedException();
    }
}