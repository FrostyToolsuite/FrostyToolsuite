using Frosty.Sdk;
using System.IO;

namespace Frosty.ModSupport;

public static class ObfuscationHeader
{
    private static readonly byte[] s_magic1 = { 0x00, 0xD1, 0xCE, 0x01 };
    private static readonly byte[] s_magic3 = { 0x00, 0xD1, 0xCE, 0x03 };

    public static void Write(Stream inStream)
    {
        if (ProfilesLibrary.FrostbiteVersion > "2014.4.11")
        {
            inStream.Write(s_magic1);
        }
        else
        {
            inStream.Write(s_magic3);
        }

        inStream.Position += 552L;
    }
}