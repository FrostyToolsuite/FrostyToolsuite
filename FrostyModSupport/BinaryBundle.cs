using System.Diagnostics;
using System.Security.Cryptography;
using Frosty.ModSupport.ModEntries;
using Frosty.ModSupport.ModInfos;
using Frosty.Sdk;
using Frosty.Sdk.DbObjectElements;
using Frosty.Sdk.Exceptions;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Utils;

namespace Frosty.ModSupport;

public static class BinaryBundle
{
    public static Block<byte> Modify(BlockStream inStream, BundleModInfo inModInfo, Dictionary<string, EbxModEntry> inModifiedEbx, Dictionary<string, ResModEntry> inModifiedRes, Dictionary<Guid, ChunkModEntry> inModifiedChunks, Action<IModEntry, int, bool> Modify)
    {
        // we use big endian for default
        Endian endian = Endian.Big;

        uint size = inStream.ReadUInt32(Endian.Big);

        long startPos = inStream.Position;

        Sdk.IO.BinaryBundle.Magic magic = (Sdk.IO.BinaryBundle.Magic)(inStream.ReadUInt32(endian) ^ Sdk.IO.BinaryBundle.GetSalt());

        bool containsSha1 = magic == Sdk.IO.BinaryBundle.Magic.Standard;

        uint totalCount = inStream.ReadUInt32(endian);
        int ebxCount = inStream.ReadInt32(endian);
        int resCount = inStream.ReadInt32(endian);
        int chunkCount = inStream.ReadInt32(endian);
        long stringsOffset = inStream.ReadUInt32(endian) + startPos;
        long metaOffset = inStream.ReadUInt32(endian) + startPos;
        inStream.Position += sizeof(int); // metaSize

        // decrypt the data
        if (magic == Sdk.IO.BinaryBundle.Magic.Encrypted)
        {
            if (!KeyManager.HasKey("BundleEncryptionKey"))
            {
                throw new MissingEncryptionKeyException("bundles");
            }

            inStream.Decrypt(KeyManager.GetKey("BundleEncryptionKey"), (int)(size - 0x20), PaddingMode.None);
        }

        EbxModEntry[] ebx = new EbxModEntry[ebxCount + inModInfo.Added.Ebx.Count];
        ResModEntry[] res = new ResModEntry[resCount + inModInfo.Added.Res.Count];
        ChunkModEntry[] chunks = new ChunkModEntry[chunkCount + inModInfo.Added.Chunks.Count];

        uint offset = 0;
        Dictionary<string, uint> strings = new(ebx.Length + res.Length);

        // read sha1s
        Sha1[] sha1 = new Sha1[totalCount + inModInfo.Added.Ebx.Count + inModInfo.Added.Res.Count + inModInfo.Added.Chunks.Count];
        int b = 0;
        for (int i = 0; i < totalCount; i++)
        {
            if (i == ebxCount)
            {
                b += inModInfo.Added.Ebx.Count;
            }
            else if (i == ebxCount + resCount)
            {
                b += inModInfo.Added.Res.Count;
            }
            sha1[i + b] = containsSha1 ? inStream.ReadSha1() : Sha1.Zero;
        }

        int j = 0;
        for (int i = 0; i < ebxCount; i++, j++)
        {
            uint nameOffset = inStream.ReadUInt32(endian);
            uint originalSize = inStream.ReadUInt32(endian);

            long currentPos = inStream.Position;
            inStream.Position = stringsOffset + nameOffset;
            string name = inStream.ReadNullTerminatedString();

            if (inModInfo.Modified.Ebx.Contains(name))
            {
                EbxModEntry modEntry = inModifiedEbx[name];
                ebx[i] = modEntry;
                Modify(modEntry, j, false);
            }
            else
            {
                ebx[i] = new EbxModEntry(name, sha1[j], originalSize);
            }

            inStream.Position = currentPos;

            if (strings.TryAdd(name, offset))
            {
                offset += (uint)name.Length + 1;
            }
        }

        int k = ebxCount;
        foreach (string name in inModInfo.Added.Ebx)
        {
            EbxModEntry modEntry = inModifiedEbx[name];
            ebx[k++] = modEntry;
            Modify(modEntry, j , true);
            sha1[j++] = modEntry.Sha1;
            if (strings.TryAdd(name, offset))
            {
                offset += (uint)name.Length + 1;
            }
        }

        long resTypeOffset = inStream.Position + resCount * 2 * sizeof(uint);
        long resMetaOffset = inStream.Position + resCount * 2 * sizeof(uint) + resCount * sizeof(uint);
        long resRidOffset = inStream.Position + resCount * 2 * sizeof(uint) + resCount * sizeof(uint) + resCount * 0x10;
        for (int i = 0; i < resCount; i++, j++)
        {
            uint nameOffset = inStream.ReadUInt32(endian);
            uint originalSize = inStream.ReadUInt32(endian);

            long currentPos = inStream.Position;
            inStream.Position = stringsOffset + nameOffset;
            string name = inStream.ReadNullTerminatedString();

            inStream.Position = resTypeOffset + i * sizeof(uint);
            uint resType = inStream.ReadUInt32();

            inStream.Position = resMetaOffset + i * 0x10;
            byte[] resMeta = inStream.ReadBytes(0x10);

            inStream.Position = resRidOffset + i * sizeof(ulong);
            ulong resRid = inStream.ReadUInt64();

            if (inModInfo.Modified.Res.Contains(name))
            {
                ResModEntry modEntry = inModifiedRes[name];
                res[i] = modEntry;
                Modify(modEntry, j, false);
            }
            else
            {
                res[i] = new ResModEntry(name, sha1[j], originalSize, resRid, resType, resMeta);
            }

            inStream.Position = currentPos;

            if (strings.TryAdd(name, offset))
            {
                offset += (uint)name.Length + 1;
            }
        }

        k = resCount;
        foreach (string name in inModInfo.Added.Res)
        {
            ResModEntry modEntry = inModifiedRes[name];
            res[k++] = modEntry;
            Modify(modEntry, j , true);
            sha1[j++] = modEntry.Sha1;

            if (strings.TryAdd(name, offset))
            {
                offset += (uint)name.Length + 1;
            }
        }

        // TODO: how to handle the meta stuff
        inStream.Position = metaOffset;
        DbObjectList chunkMeta = DbObject.Deserialize(inStream)!.AsList();

        inStream.Position = resRidOffset + resCount * sizeof(ulong);
        for (int i = 0; i < chunkCount; i++, j++)
        {
            Guid id = inStream.ReadGuid(endian);
            if (inModInfo.Modified.Chunks.Contains(id))
            {
                var modEntry = inModifiedChunks[id];
                chunks[i] = modEntry;
                Modify(modEntry, j, false);
            }
            else
            {
                chunks[i] = new ChunkModEntry(id, sha1[j], inStream.ReadUInt32(endian), inStream.ReadUInt32(endian));
            }
        }

        k = chunkCount;
        foreach (Guid id in inModInfo.Added.Chunks)
        {
            ChunkModEntry modEntry = inModifiedChunks[id];
            chunks[k++] = modEntry;
            Modify(modEntry, j , true);
            sha1[j++] = modEntry.Sha1;

            // TODO: add new chunk meta
        }

        inStream.Position = startPos + size;

        // write new bundle
        stringsOffset = 32 + (containsSha1 ? sha1.Length * 20 : 0) + ebx.Length * 8 + res.Length * 36 +
                        chunks.Length * 24; // TODO: meta first?
        Block<byte> retVal = new((int)(stringsOffset + offset));
        using (BlockStream stream = new(retVal, true))
        {
            stream.WriteUInt32(0xDEADBEEF, Endian.Big);

            stream.WriteUInt32((uint)magic ^ Sdk.IO.BinaryBundle.GetSalt(), endian);

            stream.WriteInt32(sha1.Length, endian);
            stream.WriteInt32(ebx.Length, endian);
            stream.WriteInt32(res.Length, endian);
            stream.WriteInt32(chunks.Length, endian);

            stream.WriteUInt32((uint)stringsOffset, endian);
            stream.WriteUInt32(0xDEADBEEF, endian); // TODO: metaOffset
            stream.WriteUInt32(0xDEADBEEF, endian); // TODO: metaSize

            foreach (Sha1 value in sha1)
            {
                stream.WriteSha1(value);
            }

            foreach (EbxModEntry entry in ebx)
            {
                stream.WriteUInt32(strings[entry.Name], endian);
                stream.WriteUInt32((uint)entry.OriginalSize, endian);
            }

            resTypeOffset = stream.Position + res.Length * 2 * sizeof(uint);
            resMetaOffset = stream.Position + res.Length * 2 * sizeof(uint) + res.Length * sizeof(uint);
            resRidOffset = stream.Position + res.Length * 2 * sizeof(uint) + res.Length * sizeof(uint) + res.Length * 0x10;
            for (int i = 0; i < res.Length; i++)
            {
                ResModEntry entry = res[i];
                stream.WriteUInt32(strings[entry.Name], endian);
                stream.WriteUInt32((uint)entry.OriginalSize, endian);

                long currentPos = stream.Position;
                stream.Position = resTypeOffset + i * sizeof(uint);
                stream.WriteUInt32(entry.ResType);

                stream.Position = resMetaOffset + i * 0x10;
                stream.Write(entry.ResMeta);

                stream.Position = resRidOffset + i * sizeof(ulong);
                stream.WriteUInt64(entry.ResRid);
                stream.Position = currentPos;
            }

            stream.Position = resRidOffset + res.Length * sizeof(ulong);

            foreach (ChunkModEntry entry in chunks)
            {
                stream.WriteGuid(entry.Id, endian);
                stream.WriteUInt32(entry.LogicalOffset, endian);
                stream.WriteUInt32(entry.LogicalSize, endian);
            }

            // TODO: chunk meta

            Debug.Assert(stream.Position == stringsOffset + 4);
            foreach (KeyValuePair<string,uint> pair in strings)
            {
                stream.Position = stringsOffset + 4 + pair.Value;
                stream.WriteNullTerminatedString(pair.Key);
            }
        }

        return retVal;
    }
}