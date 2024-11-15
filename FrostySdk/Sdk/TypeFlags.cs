using System;

namespace Frosty.Sdk.Sdk;

public struct TypeFlags
{
    public enum TypeEnum
    {
        Void = 0x00,
        DbObject = 0x01,
        Struct = 0x02,
        Class = 0x03,
        Array = 0x04,
        String = 0x06,
        CString = 0x07,
        Enum = 0x08,
        FileRef = 0x09,
        Boolean = 0x0A,
        Int8 = 0x0B,
        UInt8 = 0x0C,
        Int16 = 0x0D,
        UInt16 = 0x0E,
        Int32 = 0x0F,
        UInt32 = 0x10,
        Int64 = 0x11,
        UInt64 = 0x12,
        Float32 = 0x13,
        Float64 = 0x14,
        Guid = 0x15,
        Sha1 = 0x16,
        ResourceRef = 0x17,
        Function = 0x18,
        TypeRef = 0x19,
        BoxedValueRef = 0x1A,
        Interface = 0x1B,
        Delegate = 0x1C
    }

    public enum CategoryEnum
    {
        None = 0,
        Class = 1,
        Struct = 2,
        Primitive = 3, // fb < 2016
        Array = 4,
        Enum = 5,
        Function = 6, // fb < 2018
        Interface = 7,
        Delegate = 8
    }

    [Flags]
    public enum Flags
    {
        MetaData = 1 << 1,
        Homogeneous = 1 << 2,
        AlwaysPersist = 1 << 3, // only valid on fields
        Exposed = 1 << 3, // only valid on fields
        FlagsEnum = 1 << 3, // enum is a bitfield only valid on enums
        LayoutImmutable = 1 << 4,
        Blittable = 1 << 5
    }

    /// <summary>
    /// This flag is built like this, where first bit is left and last bit is right:
    /// Flags | TypeEnum | CategoryEnum | Unknown (always 1?)
    /// </summary>
    private readonly ushort m_flags;

    private static readonly int s_categoryShift = ProfilesLibrary.FrostbiteVersion >= "2018" ? 0x01 : 0x02;
    private static readonly int s_categoryMask = ProfilesLibrary.FrostbiteVersion >= "2016" ? ProfilesLibrary.FrostbiteVersion >= "2018" ? 0x0F : 0x07 : 0x03;

    private static readonly int s_typeShift = ProfilesLibrary.FrostbiteVersion >= "2016" ? 0x05 : 0x04;
    private static readonly int s_typeMask = 0x1F;

    private static readonly int s_flagsShift = 0x0A;


    public TypeFlags(ushort inFlags)
    {
        m_flags = inFlags;
    }

    public TypeFlags(TypeEnum type, CategoryEnum category = CategoryEnum.None, int flags = 0, int unk = 1)
    {
        m_flags = (ushort)((flags << s_flagsShift) | (((ushort)type & s_typeMask) << s_typeShift) |
                           (((ushort)category & s_categoryMask) << s_categoryShift) | unk);
    }

    public TypeEnum GetTypeEnum() => (TypeEnum)((m_flags >> s_typeShift) & s_typeMask);

    public CategoryEnum GetCategoryEnum() => (CategoryEnum)((m_flags >> s_categoryShift) & s_categoryMask);

    public Flags GetFlags() => (Flags)(m_flags >> s_flagsShift);

    public static implicit operator ushort(TypeFlags value) => value.m_flags;

    public static implicit operator TypeFlags(ushort value) => new(value);

}