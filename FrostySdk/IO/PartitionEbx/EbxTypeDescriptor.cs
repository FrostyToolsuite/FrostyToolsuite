﻿using System;
using Frosty.Sdk.Sdk;

namespace Frosty.Sdk.IO.PartitionEbx;

public struct EbxTypeDescriptor
{
    public string Name;
    public uint NameHash;
    public int FieldIndex;
    public byte FieldCount;
    public byte Alignment;
    public TypeFlags Flags;
    public ushort Size;
    public ushort SecondSize;

    public int Index = -1;

    public EbxTypeDescriptor(Guid inKey)
    {
        Name = string.Empty;
        ReadOnlySpan<byte> bytes = inKey.ToByteArray();
        NameHash = BitConverter.ToUInt32(bytes);
        FieldIndex = BitConverter.ToInt32(bytes[4..]);
        FieldCount = bytes[8];
        Alignment = bytes[9];
        Flags = BitConverter.ToUInt16(bytes[10..]);
        Size = BitConverter.ToUInt16(bytes[12..]);
        SecondSize = BitConverter.ToUInt16(bytes[14..]);
    }

    public ushort GetFieldCount() => (ushort)(FieldCount | ((Alignment & 0x80) << 1));
    public void SetFieldCount(ushort value) { FieldCount = (byte)value; Alignment = (byte)((Alignment & ~0x80) | ((value & 0x100) >> 1)); }
    public byte GetAlignment() => (byte)(Alignment & 0x7F);
    public void SetAlignment(byte value) => Alignment = (byte)((Alignment & ~0x7F) | value);

    public bool IsSharedTypeDescriptorKey() => (FieldIndex & 0x80000000) != 0;

    public Guid ToKey()
    {
        byte[] key = new byte[16];

        Array.Copy(BitConverter.GetBytes(NameHash), 0, key, 0, sizeof(uint));
        Array.Copy(BitConverter.GetBytes(FieldIndex), 0, key, 4, sizeof(int));
        key[8] = FieldCount;
        key[9] = Alignment;
        Array.Copy(BitConverter.GetBytes(Flags), 0, key, 10, sizeof(ushort));
        Array.Copy(BitConverter.GetBytes(Size), 0, key, 12, sizeof(ushort));
        Array.Copy(BitConverter.GetBytes(SecondSize), 0, key, 14, sizeof(ushort));

        return new Guid(key);
    }
}