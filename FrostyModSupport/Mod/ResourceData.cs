using Frosty.Sdk.Utils;

namespace Frosty.ModSupport.Mod;

public class ResourceData
{
    public long Size => m_size;
        
    private string m_fileName;
    private long m_offset;
    private int m_size;

    public ResourceData(string inFileName, long inOffset, int inSize)
    {
        m_fileName = inFileName;
        m_offset = inOffset;
        m_size = inSize;
    }

    public Block<byte> GetData()
    {
        Block<byte> retVal = new(m_size);
        using (FileStream stream = new(m_fileName, FileMode.Open, FileAccess.Read))
        {
            stream.Position = m_offset;
            stream.ReadExactly(retVal);
        }

        return retVal;
    }
}