using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Frosty.Dds;
using Frosty.Sdk;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Profiles;
using Frosty.Sdk.Resources;
using Frosty.Sdk.Utils;

namespace Frosty;

public enum TextureType
{
	TT_2d,
	TT_Cube,
	TT_3d,
	TT_2dArray,
	TT_1dArray,
	TT_1d,
	TT_CubeArray
}
public enum TextureGroupClassification
{
	Default,
	Character,
	Effect,
	UI,
	World,
	Count
}
public class Texture : Resource
{
	[Flags]
	public enum TextureFlags
	{
		Streaming = 1,
		SrgbGamma = 2
	}

	private int m_version;

	private int m_customPoolId;

	public uint Compressed2ndMipOffset { get; private set; }

	public uint Compressed3rdMipOffset { get; private set; }

	public TextureType TextureType { get; private set; }

	public int PixelFormat { get; private set; }

	public TextureFlags Flags { get; private set; }

	public ushort Width { get; private set; }

	public ushort Height { get; private set; }

	public ushort Depth { get; private set; }

	public ushort SliceCount { get; private set; }

	public byte MipMapCount { get; private set; }

	public byte FirstMip { get; private set; }

	public uint[] MipSizes { get; } = new uint[15];


	public Guid ChunkId { get; private set; }

	public uint ChunkSize { get; private set; }

	public uint[] Unk { get; } = new uint[4];


	public uint NameHash { get; private set; }

	public TextureGroupClassification TextureGroupClassification { get; private set; }

	public string TextureGroup { get; private set; } = string.Empty;


	public override void Deserialize(DataStream inStream, ReadOnlySpan<byte> inResMeta)
	{
		m_version = BinaryPrimitives.ReadInt32LittleEndian(inResMeta);
		if (m_version == 0)
		{
			m_version = inStream.ReadInt32();
		}
		Debug.Assert(10 <= m_version && m_version <= 13, "Unsupported Texture version");
		if (m_version >= 11)
		{
			Compressed2ndMipOffset = inStream.ReadUInt32();
			Compressed3rdMipOffset = inStream.ReadUInt32();
		}
		TextureType = (TextureType)inStream.ReadUInt32();
		PixelFormat = inStream.ReadInt32();
		if (m_version >= 12)
		{
			m_customPoolId = inStream.ReadInt32();
		}
		Flags = (TextureFlags)((m_version >= 11) ? inStream.ReadUInt16() : inStream.ReadUInt32());
		Width = inStream.ReadUInt16();
		Height = inStream.ReadUInt16();
		Depth = inStream.ReadUInt16();
		if (ProfilesLibrary.FrostbiteVersion >= "2021.2.3")
		{
			byte b = inStream.ReadByte();
		}
		else
		{
			SliceCount = inStream.ReadUInt16();
		}
		if (m_version < 11)
		{
			inStream.Position += 2L;
		}
		MipMapCount = inStream.ReadByte();
		FirstMip = inStream.ReadByte();
		if (ProfilesLibrary.FrostbiteVersion >= "2021.2.3")
		{
			byte b2 = inStream.ReadByte();
		}
		if (ProfilesLibrary.FrostbiteVersion >= "2021.1.1")
		{
			byte b3 = inStream.ReadByte();
			byte b4 = inStream.ReadByte();
			bool flag = inStream.ReadBoolean();
		}
		inStream.Pad(4);
		ChunkId = inStream.ReadGuid();
		for (int i = 0; i < 15; i++)
		{
			MipSizes[i] = inStream.ReadUInt32();
		}
		ChunkSize = inStream.ReadUInt32();
		if (m_version >= 13)
		{
			for (int j = 0; j < 4; j++)
			{
				Unk[j] = inStream.ReadUInt32();
			}
		}
		NameHash = inStream.ReadUInt32();
		if (ProfilesLibrary.IsLoaded(ProfileVersion.PlantsVsZombiesGardenWarfare2))
		{
			TextureGroupClassification = (TextureGroupClassification)inStream.ReadInt32();
		}
		TextureGroup = inStream.ReadFixedSizedString(16).TrimEnd('\0');
		if (ProfilesLibrary.FrostbiteVersion >= "2021.2.3")
		{
			inStream.ReadInt32();
		}
	}

	public override void Serialize(DataStream inStream, Span<byte> resMeta)
	{
		Debug.Assert(10 <= m_version && m_version <= 13, "Unsupported Texture version");
		if (m_version < 11)
		{
			inStream.WriteInt32(m_version);
		}
		else
		{
			BinaryPrimitives.WriteInt32LittleEndian(resMeta, m_version);
		}
		if (m_version >= 11)
		{
			inStream.WriteUInt32(Compressed2ndMipOffset);
			inStream.WriteUInt32(Compressed3rdMipOffset);
		}
		inStream.WriteUInt32((uint)TextureType);
		inStream.WriteInt32(PixelFormat);
		if (m_version >= 12)
		{
			inStream.WriteInt32(m_customPoolId);
		}
		if (m_version >= 11)
		{
			inStream.WriteUInt16((ushort)Flags);
			BinaryPrimitives.WriteUInt32LittleEndian(resMeta.Slice(4, resMeta.Length - 4), (uint)Flags);
		}
		else
		{
			inStream.WriteUInt32((uint)Flags);
		}
		inStream.WriteUInt16(Width);
		inStream.WriteUInt16(Height);
		inStream.WriteUInt16(Depth);
		inStream.WriteUInt16(SliceCount);
		if (m_version < 11)
		{
			inStream.Position += 2L;
		}
		inStream.WriteByte(MipMapCount);
		inStream.WriteByte(FirstMip);
		inStream.WriteGuid(ChunkId);
		for (int i = 0; i < 15; i++)
		{
			inStream.WriteUInt32(MipSizes[i]);
		}
		inStream.WriteUInt32(ChunkSize);
		if (m_version >= 13)
		{
			for (int j = 0; j < 4; j++)
			{
				inStream.WriteUInt32(Unk[j]);
			}
		}
		inStream.WriteUInt32(NameHash);
		if (ProfilesLibrary.IsLoaded(ProfileVersion.PlantsVsZombiesGardenWarfare2))
		{
			inStream.WriteInt32((int)TextureGroupClassification);
		}
		Span<byte> span = stackalloc byte[16];
		Encoding.ASCII.GetBytes(TextureGroup, span);
		for (int k = TextureGroup.Length; k < 15; k++)
		{
			span[k] = 0;
		}
		span[span.Length - 1] = 0;
		inStream.Write(span);
	}

	public string GetPixelFormatEnum()
	{
		bool isLegacyEnum = false;
		Type? type = TypeLibrary.GetType("RenderFormat");
		if (type is null)
		{
			type = TypeLibrary.GetType("TextureFormat");
			if (type is null)
			{
				throw new Exception("No RenderFormat type found in TypeInfo");
			}
			isLegacyEnum = true;
		}
		string? retVal = Enum.Parse(type, PixelFormat.ToString()).ToString();
		if (retVal is null)
		{
			throw new Exception($"Invalid RenderFormat {PixelFormat}");
		}

		return isLegacyEnum ? ConvertToRenderFormat(retVal) : retVal;
	}

	private string ConvertToRenderFormat(string inTextureFormat)
	{
		string srgb = Flags.HasFlag(TextureFlags.SrgbGamma) ? "SRGB" : "UNORM";
        switch (inTextureFormat)
        {
            case "TextureFormat_DXT1":
                return $"RenderFormat_BC1_{srgb}";
            case "TextureFormat_DXT1A":
                return $"RenderFormat_BC1A_{srgb}";
            case "TextureFormat_DXT3":
                return $"RenderFormat_BC2_{srgb}";
            case "TextureFormat_DXT5":
                return $"RenderFormat_BC3_{srgb}";
            case "TextureFormat_DXT5A":
                return "RenderFormat_BC4_UNORM";
            case "TextureFormat_DXN":
                return "RenderFormat_BC5_UNORM";
            case "TextureFormat_BC7":
                return $"RenderFormat_BC7_{srgb}";
            case "TextureFormat_RGB565":
                return "RenderFormat_R5G6B5_UNORM";
            case "TextureFormat_RGB888":
                return "RenderFormat_R8G8B8_UNORM";
            case "TextureFormat_ARGB1555":
                return "RenderFormat_R5G5B5A1_UNORM";
            case "TextureFormat_ARGB4444":
                return "RenderFormat_R4G4B4A4_UNORM";
            case "TextureFormat_ARGB8888":
                return $"RenderFormat_R8G8B8A8_{srgb}";
            case "TextureFormat_L8":
                return "RenderFormat_R8_UNORM";
            case "TextureFormat_L16":
                return "RenderFormat_R16_UNORM";
            case "TextureFormat_ABGR16":
                return "RenderFormat_R16G16B16A16_UNORM";
            case "TextureFormat_ABGR16F":
                return "RenderFormat_R16G16B16A16_FLOAT";
            case "TextureFormat_ABGR32F":
                return "RenderFormat_R32G32B32A32_FLOAT";
            case "TextureFormat_R16F":
                return "RenderFormat_R16_FLOAT";
            case "TextureFormat_R32F":
                return "RenderFormat_R32_FLOAT";
            case "TextureFormat_NormalDXN":
                return "RenderFormat_BC5_UNORM";
            case "TextureFormat_NormalDXT1":
                return "RenderFormat_BC1_UNORM";
            case "TextureFormat_NormalDXT5":
                return "RenderFormat_BC3_UNORM";
            case "TextureFormat_NormalDXT5RGA":
                return "RenderFormat_BC4_UNORM";
            case "TextureFormat_RG8":
                return "RenderFormat_R8G8_UNORM";
            case "TextureFormat_GR16":
                return "RenderFormat_R16G16_UINT";
            case "TextureFormat_GR16F":
                return "RenderFormat_R16G16_FLOAT";
            case "TextureFormat_D16":
                return "RenderFormat_D16_UNORM";
            case "TextureFormat_D24":
                return "RenderFormat_D24_UNORM";
            case "TextureFormat_D24S8":
                return "RenderFormat_D24_UNORM_S8_UINT";
            case "TextureFormat_D24FS8":
                return "RenderFormat_D24_FLOAT_S8_UINT";
            case "TextureFormat_D32F":
                return "RenderFormat_D32_FLOAT";
            case "TextureFormat_D32FS8":
                return "RenderFormat_D32_FLOAT_S8_UINT";
            case "TextureFormat_S8":
                return "RenderFormat_R8_UNORM";
            case "TextureFormat_ABGR32":
                return "RenderFormat_R32G32B32A32_UINT";
            case "TextureFormat_GR32F":
                return "RenderFormat_R32G32_FLOAT";
            case "TextureFormat_A2R10G10B10":
                return "RenderFormat_R10G10B10A2_UNORM";
            case "TextureFormat_R11G11B10F":
                return "RenderFormat_R11G11B10_FLOAT";
            case "TextureFormat_ABGR16_SNORM":
                return "RenderFormat_R16G16B16A16_SNORM";
            case "TextureFormat_ABGR16_UINT":
                return "RenderFormat_R16G16B16A16_UINT";
            case "TextureFormat_L16_UINT":
                return "RenderFormat_R16_UINT";
            case "TextureFormat_L32":
                return "RenderFormat_R32_UINT";
            case "TextureFormat_GR16_UINT":
                return "RenderFormat_R16G16_UINT";
            case "TextureFormat_GR32_UINT":
                return "RenderFormat_R32G32_UINT";
            case "TextureFormat_ETC1":
                return $"RenderFormat_ETC1_{srgb}";
            case "TextureFormat_ETC2_RGB":
                return $"RenderFormat_ETC2RGB_{srgb}";
            case "TextureFormat_ETC2_RGBA":
                return $"RenderFormat_ETC2RGBA{srgb}";
            case "TextureFormat_ETC2_RGB_A1":
                return $"RenderFormat_ETC2RGBA1_{srgb}";
            case "TextureFormat_PVRTC1_4BPP_RGBA":
                return $"RenderFormat_PVRTC1_4BPP_RGBA_{srgb}";
            case "TextureFormat_PVRTC1_4BPP_RGB":
                return $"RenderFormat_PVRTC1_4BPP_RGB_{srgb}";
            case "TextureFormat_PVRTC1_2BPP_RGBA":
                return $"RenderFormat_PVRTC1_2BPP_RGBA_{srgb}";
            case "TextureFormat_PVRTC1_2BPP_RGB":
                return $"RenderFormat_PVRTC1_2BPP_RGB_{srgb}";
            case "TextureFormat_PVRTC2_4BPP":
                return $"RenderFormat_PVRTC2_4BPP_{srgb}";
            case "TextureFormat_PVRTC2_2BPP":
                return $"RenderFormat_PVRTC2_2BPP_{srgb}";
            case "TextureFormat_R8":
                return "RenderFormat_R8_UNORM";
            case "TextureFormat_R9G9B9E5F":
                    return "RenderFormat_R9G9B9E5_FLOAT";
            case "TextureFormat_Unknown":
                return "RenderFormat_Unknown";
            default:
                throw new NotImplementedException();
        }
	}

	public static (DdsHeader, DdsHeaderDx10) ToDdsHeader(Texture texture)
	{
		DdsHeader ddsHeader = new()
        {
            Magic = 542327876u,
            Size = 124u,
            Flags = DdsHeaderFlags.Texture,
            Height = texture.Height,
            Width = texture.Width,
            PitchOrLinearSize = texture.MipSizes[0],
            MipMapCount = texture.MipMapCount,
            PixelFormat = DdsPixelFormat.Dx10
        };
		if (texture.MipMapCount > 1)
		{
            ddsHeader.Flags |= DdsHeaderFlags.MipMapCount;
            ddsHeader.Caps |= DdsCaps.Complex | DdsCaps.MipMap;
		}
		DdsHeaderDx10 dx10Header = new();
		switch (texture.TextureType)
		{
		case TextureType.TT_2d:
			dx10Header.ResourceDimension = D3D10ResourceDimension.Texture2D;
			dx10Header.ArraySize = 1u;
			break;
		case TextureType.TT_2dArray:
			dx10Header.ResourceDimension = D3D10ResourceDimension.Texture2D;
			dx10Header.ArraySize = texture.Depth;
			break;
		case TextureType.TT_Cube:
            ddsHeader.Caps2 = DdsCaps2.CubeMapAllFaces | DdsCaps2.CubeMap;
			dx10Header.ResourceDimension = D3D10ResourceDimension.Texture2D;
			dx10Header.ArraySize = 1u;
			dx10Header.MiscFlag = 4u;
			break;
		case TextureType.TT_3d:
            ddsHeader.Flags |= DdsHeaderFlags.Depth;
            ddsHeader.Caps2 |= DdsCaps2.Volume;
            ddsHeader.Depth = texture.Depth;
			dx10Header.ResourceDimension = D3D10ResourceDimension.Texture3D;
			dx10Header.ArraySize = 1u;
			break;
		case TextureType.TT_1d:
			dx10Header.ResourceDimension = D3D10ResourceDimension.Texture1D;
			dx10Header.ArraySize = 1u;
			break;
		case TextureType.TT_1dArray:
			dx10Header.ResourceDimension = D3D10ResourceDimension.Texture1D;
			dx10Header.ArraySize = texture.Depth;
			break;
		case TextureType.TT_CubeArray:
            ddsHeader.Caps2 = DdsCaps2.CubeMapAllFaces | DdsCaps2.CubeMap;
			dx10Header.ResourceDimension = D3D10ResourceDimension.Texture2D;
			dx10Header.ArraySize = texture.Depth;
			dx10Header.MiscFlag = 4u;
			break;
		}
		dx10Header.DxgiFormat = TextureUtils.ToDxgiFormat(texture.GetPixelFormatEnum());
		return (ddsHeader, dx10Header);
	}

	public bool SaveDds(string inPath)
	{
		ChunkAssetEntry? chunkAssetEntry = AssetManager.GetChunkAssetEntry(ChunkId);
		if (chunkAssetEntry is null)
		{
			return false;
		}
		using Block<byte> block = AssetManager.GetAsset(chunkAssetEntry);
		(DdsHeader header, DdsHeaderDx10 dx10Header) = ToDdsHeader(this);
		using DataStream dataStream = new(File.Create(inPath));
		Span<byte> span = stackalloc byte[128];
		MemoryMarshal.Write(span, in header);
		dataStream.Write(span);
		MemoryMarshal.Write(span, in dx10Header);
		dataStream.Write(span[..20]);
		dataStream.Write(block);
		return true;
	}

	public bool SaveKtx(string inPath)
	{
		throw new NotImplementedException();
	}
}