using System;
using System.IO;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers.Infos.FileInfos;
using Frosty.Sdk.Utils;

namespace Frosty.Sdk.Interfaces;

public interface IFileInfo
{
    public bool IsDelta();

    public bool IsComplete();

    public bool FileExists();

    public long GetOriginalSize();

    public Block<byte> GetRawData();

    public Block<byte> GetData(int inOriginalSize);

    protected void SerializeInternal(DataStream stream);

    public static void Serialize(DataStream stream, IFileInfo fileInfo)
    {
        switch (fileInfo)
        {
            case CasFileInfo:
                stream.WriteByte(0);
                break;
            case KelvinFileInfo:
                stream.WriteByte(1);
                break;
            case NonCasFileInfo:
                stream.WriteByte(2);
                break;
            default:
                throw new NotImplementedException();
        }
        fileInfo.SerializeInternal(stream);
    }

    public static IFileInfo Deserialize(DataStream stream)
    {
        byte type = stream.ReadByte();

        switch (type)
        {
            case 0:
                return CasFileInfo.DeserializeInternal(stream);
            case 1:
                return KelvinFileInfo.DeserializeInternal(stream);
            case 2:
                return NonCasFileInfo.DeserializeInternal(stream);
            default:
                throw new InvalidDataException();
        }
    }
}