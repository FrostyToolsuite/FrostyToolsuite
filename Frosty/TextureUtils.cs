using Frosty.Dds;
using Frosty.Ktx;
using Frosty.Sdk;
using Microsoft.Extensions.Logging;

namespace Frosty;

public static class TextureUtils
	{
		public static DxgiFormat ToDxgiFormat(string inRenderFormat)
		{
            // TODO: clean up, ilspy couldnt recover the switch case, see ToVkFormat
			if (inRenderFormat != null)
			{
				switch (inRenderFormat.Length)
				{
				case 20:
					switch (inRenderFormat[16])
					{
					case 'n':
						if (!(inRenderFormat == "RenderFormat_Unknown"))
						{
							break;
						}
						return DxgiFormat.Unknown;
					case 'U':
						if (!(inRenderFormat == "RenderFormat_R8_UINT"))
						{
							break;
						}
						return DxgiFormat.R8Uint;
					case 'S':
						if (!(inRenderFormat == "RenderFormat_R8_SINT"))
						{
							break;
						}
						return DxgiFormat.R8Sint;
					}
					break;
				case 25:
					switch (inRenderFormat[14])
					{
					case '5':
						if (inRenderFormat != "RenderFormat_B5G6R5_UNORM")
						{
							break;
						}
						return DxgiFormat.B5G6R5Unorm;
					case '1':
						switch (inRenderFormat)
						{
						case "RenderFormat_R16G16_FLOAT":
							return DxgiFormat.R16G16Float;
						case "RenderFormat_R16G16_UNORM":
							return DxgiFormat.R16G16Unorm;
						case "RenderFormat_R16G16_SNORM":
							return DxgiFormat.R16G16Snorm;
						}
						break;
					case '3':
						if (!(inRenderFormat == "RenderFormat_R32G32_FLOAT"))
						{
							break;
						}
						return DxgiFormat.R32G32Float;
					}
					break;
				case 21:
					switch (inRenderFormat[15])
					{
					case '_':
						if (!(inRenderFormat == "RenderFormat_R8_UNORM"))
						{
							if (!(inRenderFormat == "RenderFormat_R8_SNORM"))
							{
								break;
							}
							return DxgiFormat.R8Snorm;
						}
						return DxgiFormat.R8Unorm;
					case '6':
						if (!(inRenderFormat == "RenderFormat_R16_UINT"))
						{
							if (!(inRenderFormat == "RenderFormat_R16_SINT"))
							{
								break;
							}
							return DxgiFormat.R16Sint;
						}
						return DxgiFormat.R16Uint;
					case '2':
						switch (inRenderFormat)
						{
						case "RenderFormat_R32_UINT":
							return DxgiFormat.R32Uint;
						case "RenderFormat_R32_SINT":
							return DxgiFormat.R32Sint;
						case "RenderFormat_BC2_SRGB":
							return DxgiFormat.Bc2UnormSrgb;
						}
						break;
					case '1':
						if (!(inRenderFormat == "RenderFormat_BC1_SRGB"))
						{
							break;
						}
						return DxgiFormat.Bc1UnormSrgb;
					case '3':
						if (!(inRenderFormat == "RenderFormat_BC3_SRGB"))
						{
							break;
						}
						return DxgiFormat.Bc3UnormSrgb;
					case '7':
						if (!(inRenderFormat == "RenderFormat_BC7_SRGB"))
						{
							break;
						}
						return DxgiFormat.Bc7UnormSrgb;
					}
					break;
				case 23:
					switch (inRenderFormat[16])
					{
					case '8':
						if (!(inRenderFormat == "RenderFormat_R8G8_UNORM"))
						{
							if (!(inRenderFormat == "RenderFormat_R8G8_SNORM"))
							{
								break;
							}
							return DxgiFormat.R8G8Snorm;
						}
						return DxgiFormat.R8G8Unorm;
					case 'A':
						if (!(inRenderFormat == "RenderFormat_BC1A_UNORM"))
						{
							break;
						}
						return DxgiFormat.Bc1Unorm;
					case 'U':
						if (!(inRenderFormat == "RenderFormat_BC6U_FLOAT"))
						{
							break;
						}
						return DxgiFormat.Bc6HUf16;
					case 'S':
						if (!(inRenderFormat == "RenderFormat_BC6S_FLOAT"))
						{
							break;
						}
						return DxgiFormat.Bc6HSf16;
					}
					break;
				case 22:
					switch (inRenderFormat[15])
					{
					case 'G':
						if (!(inRenderFormat == "RenderFormat_R8G8_UINT"))
						{
							if (!(inRenderFormat == "RenderFormat_R8G8_SINT"))
							{
								break;
							}
							return DxgiFormat.R8G8Sint;
						}
						return DxgiFormat.R8G8Uint;
					case '6':
						switch (inRenderFormat)
						{
						case "RenderFormat_R16_FLOAT":
							return DxgiFormat.R16Float;
						case "RenderFormat_R16_UNORM":
							return DxgiFormat.R16Unorm;
						case "RenderFormat_R16_SNORM":
							return DxgiFormat.R16Snorm;
						case "RenderFormat_D16_UNORM":
							return DxgiFormat.D16Unorm;
						}
						break;
					case '2':
						switch (inRenderFormat)
						{
						case "RenderFormat_R32_FLOAT":
							return DxgiFormat.R32Float;
						case "RenderFormat_BC2_UNORM":
							return DxgiFormat.Bc2Unorm;
						case "RenderFormat_D32_FLOAT":
							return DxgiFormat.D32Float;
						}
						break;
					case '1':
						if (!(inRenderFormat == "RenderFormat_BC1_UNORM"))
						{
							if (!(inRenderFormat == "RenderFormat_BC1A_SRGB"))
							{
								break;
							}
							return DxgiFormat.Bc1UnormSrgb;
						}
						return DxgiFormat.Bc1Unorm;
					case '3':
						if (!(inRenderFormat == "RenderFormat_BC3_UNORM"))
						{
							break;
						}
						return DxgiFormat.Bc3Unorm;
					case '4':
						if (!(inRenderFormat == "RenderFormat_BC4_UNORM"))
						{
							break;
						}
						return DxgiFormat.Bc4Unorm;
					case '5':
						if (!(inRenderFormat == "RenderFormat_BC5_UNORM"))
						{
							break;
						}
						return DxgiFormat.Bc5Unorm;
					case '7':
						if (!(inRenderFormat == "RenderFormat_BC7_UNORM"))
						{
							break;
						}
						return DxgiFormat.Bc7Unorm;
					}
					break;
				case 27:
					switch (inRenderFormat[22])
					{
					case 'U':
						if (!(inRenderFormat == "RenderFormat_R8G8B8A8_UNORM"))
						{
							if (!(inRenderFormat == "RenderFormat_B8G8R8A8_UNORM"))
							{
								break;
							}
							return DxgiFormat.B8G8R8A8Unorm;
						}
						return DxgiFormat.R8G8B8A8Unorm;
					case 'S':
						if (!(inRenderFormat == "RenderFormat_R8G8B8A8_SNORM"))
						{
							break;
						}
						return DxgiFormat.R8G8B8A8Snorm;
					case 'F':
						if (!(inRenderFormat == "RenderFormat_R9G9B9E5_FLOAT"))
						{
							break;
						}
						return DxgiFormat.R9G9B9E5Sharedexp;
					}
					break;
				case 26:
					switch (inRenderFormat[13])
					{
					case 'R':
						switch (inRenderFormat)
						{
						case "RenderFormat_R8G8B8A8_SRGB":
							return DxgiFormat.R8G8B8A8UnormSrgb;
						case "RenderFormat_R8G8B8A8_UINT":
							return DxgiFormat.R8G8B8A8Uint;
						case "RenderFormat_R8G8B8A8_SINT":
							return DxgiFormat.R8G8B8A8Sint;
						}
						break;
					case 'B':
						if (!(inRenderFormat == "RenderFormat_B8G8R8A8_SRGB"))
						{
							break;
						}
						return DxgiFormat.R8G8B8A8UnormSrgb;
					}
					break;
				case 30:
					switch (inRenderFormat[18])
					{
					case '0':
						if (!(inRenderFormat == "RenderFormat_R10G10B10A2_UNORM"))
						{
							break;
						}
						return DxgiFormat.R10G10B10A2Unorm;
					case '6':
						if (!(inRenderFormat == "RenderFormat_R16G16B16A16_UINT"))
						{
							if (!(inRenderFormat == "RenderFormat_R16G16B16A16_SINT"))
							{
								break;
							}
							return DxgiFormat.R16G16B16A16Sint;
						}
						return DxgiFormat.R16G16B16A16Uint;
					case '2':
						if (!(inRenderFormat == "RenderFormat_R32G32B32A32_UINT"))
						{
							if (!(inRenderFormat == "RenderFormat_R32G32B32A32_SINT"))
							{
								break;
							}
							return DxgiFormat.R32G32B32A32Sint;
						}
						return DxgiFormat.R32G32B32A32Uint;
					case 'N':
						if (!(inRenderFormat == "RenderFormat_D24_UNORM_S8_UINT"))
						{
							break;
						}
						return DxgiFormat.D24UnormS8Uint;
					case 'L':
						if (!(inRenderFormat == "RenderFormat_D32_FLOAT_S8_UINT"))
						{
							break;
						}
						return DxgiFormat.D32FloatS8X24Uint;
					}
					break;
				case 24:
					switch (inRenderFormat[14])
					{
					case '1':
						if (!(inRenderFormat == "RenderFormat_R16G16_UINT"))
						{
							if (!(inRenderFormat == "RenderFormat_R16G16_SINT"))
							{
								break;
							}
							return DxgiFormat.R16G16Sint;
						}
						return DxgiFormat.R16G16Uint;
					case '3':
						if (!(inRenderFormat == "RenderFormat_R32G32_UINT"))
						{
							if (!(inRenderFormat == "RenderFormat_R32G32_SINT"))
							{
								break;
							}
							return DxgiFormat.R32G32Sint;
						}
						return DxgiFormat.R32G32Uint;
					}
					break;
				case 31:
					switch (inRenderFormat[26])
					{
					case 'F':
						if (!(inRenderFormat == "RenderFormat_R16G16B16A16_FLOAT"))
						{
							if (!(inRenderFormat == "RenderFormat_R32G32B32A32_FLOAT"))
							{
								break;
							}
							return DxgiFormat.R32G32B32A32Float;
						}
						return DxgiFormat.R16G16B16A16Float;
					case 'U':
						if (!(inRenderFormat == "RenderFormat_R16G16B16A16_UNORM"))
						{
							break;
						}
						return DxgiFormat.R16G16B16A16Unorm;
					case 'S':
						if (!(inRenderFormat == "RenderFormat_R16G16B16A16_SNORM"))
						{
							break;
						}
						return DxgiFormat.R16G16B16A16Snorm;
					}
					break;
				case 28:
					if (!(inRenderFormat == "RenderFormat_R11G11B10_FLOAT"))
					{
						break;
					}
					return DxgiFormat.R11G11B10Float;
				case 29:
					if (!(inRenderFormat == "RenderFormat_R10G10B10A2_UINT"))
					{
						break;
					}
					return DxgiFormat.R10G10B10A2Uint;
				}
			}
			FrostyLogger.Logger?.LogError("{} is not supported by DXGI_FORMAT", inRenderFormat);
			return DxgiFormat.Unknown;
		}

		public static VkFormat ToVkFormat(string inRenderFormat)
		{
			switch (inRenderFormat)
			{
			    case "RenderFormat_Unknown":
				    return VkFormat.Undefined;
			    case "RenderFormat_R4G4_UNORM":
				    return VkFormat.R4G4UnormPack8;
			    case "RenderFormat_R4G4B4A4_UNORM":
				    return VkFormat.R4G4B4A4UnormPack16;
			    case "RenderFormat_R5G6B5_UNORM":
				    return VkFormat.R5G6B5UnormPack16;
			    case "RenderFormat_B5G6R5_UNORM":
				    return VkFormat.B5G6R5UnormPack16;
			    case "RenderFormat_R5G5B5A1_UNORM":
				    return VkFormat.R5G5B5A1UnormPack16;
			    case "RenderFormat_R8_UNORM":
				    return VkFormat.R8Unorm;
			    case "RenderFormat_R8_SNORM":
				    return VkFormat.R8Snorm;
			    case "RenderFormat_R8_UINT":
				    return VkFormat.R8Uint;
			    case "RenderFormat_R8_SINT":
				    return VkFormat.R8Sint;
			    case "RenderFormat_R8G8_UNORM":
				    return VkFormat.R8G8Unorm;
			    case "RenderFormat_R8G8_SNORM":
				    return VkFormat.R8G8Snorm;
			    case "RenderFormat_R8G8_UINT":
				    return VkFormat.R8G8Uint;
			    case "RenderFormat_R8G8_SINT":
				    return VkFormat.R8G8Sint;
			    case "RenderFormat_R8G8B8_UNORM":
				    return VkFormat.R8G8B8Unorm;
			    case "RenderFormat_R8G8B8_SRGB":
				    return VkFormat.R8G8B8Srgb;
			    case "RenderFormat_R8G8B8A8_UNORM":
				    return VkFormat.R8G8B8A8Unorm;
			    case "RenderFormat_R8G8B8A8_SNORM":
				    return VkFormat.R8G8B8A8Snorm;
			    case "RenderFormat_R8G8B8A8_SRGB":
				    return VkFormat.R8G8B8A8Srgb;
			    case "RenderFormat_R8G8B8A8_UINT":
				    return VkFormat.R8G8B8A8Uint;
			    case "RenderFormat_R8G8B8A8_SINT":
				    return VkFormat.R8G8B8A8Sint;
			    case "RenderFormat_B8G8R8A8_UNORM":
				    return VkFormat.B8G8R8A8Unorm;
			    case "RenderFormat_B8G8R8A8_SRGB":
				    return VkFormat.B8G8R8A8Srgb;
			    case "RenderFormat_R11G11B10_FLOAT":
				    return VkFormat.B10G11R11UfloatPack32;
			    case "RenderFormat_R10G10B10A2_UNORM":
				    return VkFormat.A2R10G10B10UnormPack32;
			    case "RenderFormat_R10G10B10A2_UINT":
				    return VkFormat.A2R10G10B10UintPack32;
			    case "RenderFormat_R9G9B9E5_FLOAT":
				    return VkFormat.E5B9G9R9UfloatPack32;
			    case "RenderFormat_R16_FLOAT":
				    return VkFormat.R16Sfloat;
			    case "RenderFormat_R16_UNORM":
				    return VkFormat.R16Unorm;
			    case "RenderFormat_R16_SNORM":
				    return VkFormat.R16Snorm;
			    case "RenderFormat_R16_UINT":
				    return VkFormat.R16Uint;
			    case "RenderFormat_R16_SINT":
				    return VkFormat.R16Sint;
			    case "RenderFormat_R16G16_FLOAT":
				    return VkFormat.R16G16Sfloat;
			    case "RenderFormat_R16G16_UNORM":
				    return VkFormat.R16G16Unorm;
			    case "RenderFormat_R16G16_SNORM":
				    return VkFormat.R16G16Snorm;
			    case "RenderFormat_R16G16_UINT":
				    return VkFormat.R16G16Uint;
			    case "RenderFormat_R16G16_SINT":
				    return VkFormat.R16G16Sint;
			    case "RenderFormat_R16G16B16A16_FLOAT":
				    return VkFormat.R16G16B16A16Sfloat;
			    case "RenderFormat_R16G16B16A16_UNORM":
				    return VkFormat.R16G16B16A16Unorm;
			    case "RenderFormat_R16G16B16A16_SNORM":
				    return VkFormat.R16G16B16A16Snorm;
			    case "RenderFormat_R16G16B16A16_UINT":
				    return VkFormat.R16G16B16A16Uint;
			    case "RenderFormat_R16G16B16A16_SINT":
				    return VkFormat.R16G16B16A16Sint;
			    case "RenderFormat_R32_FLOAT":
				    return VkFormat.R32Sfloat;
			    case "RenderFormat_R32_UINT":
				    return VkFormat.R32Uint;
			    case "RenderFormat_R32_SINT":
				    return VkFormat.R32Sint;
			    case "RenderFormat_R32G32_FLOAT":
				    return VkFormat.R32G32Sfloat;
			    case "RenderFormat_R32G32_UINT":
				    return VkFormat.R32G32Uint;
			    case "RenderFormat_R32G32_SINT":
				    return VkFormat.R32G32Sint;
			    case "RenderFormat_R32G32B32A32_FLOAT":
				    return VkFormat.R32G32B32A32Sfloat;
			    case "RenderFormat_R32G32B32A32_UINT":
				    return VkFormat.R32G32B32A32Uint;
			    case "RenderFormat_R32G32B32A32_SINT":
				    return VkFormat.R32G32B32A32Sint;
			    case "RenderFormat_BC1_UNORM":
				    return VkFormat.Bc1RgbUnormBlock;
			    case "RenderFormat_BC1_SRGB":
				    return VkFormat.Bc1RgbSrgbBlock;
			    case "RenderFormat_BC1A_UNORM":
				    return VkFormat.Bc1RgbaUnormBlock;
			    case "RenderFormat_BC1A_SRGB":
				    return VkFormat.Bc1RgbaSrgbBlock;
			    case "RenderFormat_BC2_UNORM":
				    return VkFormat.Bc2UnormBlock;
			    case "RenderFormat_BC2_SRGB":
				    return VkFormat.Bc2SrgbBlock;
			    case "RenderFormat_BC3_UNORM":
				    return VkFormat.Bc3UnormBlock;
			    case "RenderFormat_BC3_SRGB":
				    return VkFormat.Bc3SrgbBlock;
			    case "RenderFormat_BC4_UNORM":
				    return VkFormat.Bc4UnormBlock;
			    case "RenderFormat_BC5_UNORM":
				    return VkFormat.Bc5UnormBlock;
			    case "RenderFormat_BC6U_FLOAT":
				    return VkFormat.Bc6HUfloatBlock;
			    case "RenderFormat_BC6S_FLOAT":
				    return VkFormat.Bc6HSfloatBlock;
			    case "RenderFormat_BC7_UNORM":
				    return VkFormat.Bc7UnormBlock;
			    case "RenderFormat_BC7_SRGB":
				    return VkFormat.Bc7SrgbBlock;
			    case "RenderFormat_ETC2RGB_UNORM":
				    return VkFormat.Etc2R8G8B8UnormBlock;
			    case "RenderFormat_ETC2RGB_SRGB":
				    return VkFormat.Etc2R8G8B8SrgbBlock;
			    case "RenderFormat_ETC2RGBA_UNORM":
				    return VkFormat.Etc2R8G8B8A8UnormBlock;
			    case "RenderFormat_ETC2RGBA_SRGB":
				    return VkFormat.Etc2R8G8B8A8SrgbBlock;
			    case "RenderFormat_ETC2RGBA1_UNORM":
				    return VkFormat.Etc2R8G8B8A1UnormBlock;
			    case "RenderFormat_ETC2RGBA1_SRGB":
				    return VkFormat.Etc2R8G8B8A1SrgbBlock;
			    case "RenderFormat_EAC_R11_UNORM":
				    return VkFormat.EacR11UnormBlock;
			    case "RenderFormat_EAC_R11_SNORM":
				    return VkFormat.EacR11SnormBlock;
			    case "RenderFormat_EAC_RG11_UNORM":
				    return VkFormat.EacR11G11UnormBlock;
			    case "RenderFormat_EAC_RG11_SNORM":
				    return VkFormat.EacR11G11SnormBlock;
			    case "RenderFormat_PVRTC1_4BPP_RGBA_UNORM":
				    return VkFormat.Pvrtc14BppUnormBlockImg;
			    case "RenderFormat_PVRTC1_4BPP_RGBA_SRGB":
				    return VkFormat.Pvrtc14BppSrgbBlockImg;
			    case "RenderFormat_PVRTC1_2BPP_RGBA_UNORM":
				    return VkFormat.Pvrtc12BppUnormBlockImg;
			    case "RenderFormat_PVRTC1_2BPP_RGBA_SRGB":
				    return VkFormat.Pvrtc12BppSrgbBlockImg;
			    case "RenderFormat_PVRTC2_4BPP_UNORM":
				    return VkFormat.Pvrtc24BppUnormBlockImg;
			    case "RenderFormat_PVRTC2_4BPP_SRGB":
				    return VkFormat.Pvrtc24BppSrgbBlockImg;
			    case "RenderFormat_PVRTC2_2BPP_UNORM":
				    return VkFormat.Pvrtc22BppUnormBlockImg;
			    case "RenderFormat_PVRTC2_2BPP_SRGB":
				    return VkFormat.Pvrtc22BppSrgbBlockImg;
			    case "RenderFormat_ASTC_4x4_UNORM":
				    return VkFormat.Astc4X4UnormBlock;
			    case "RenderFormat_ASTC_4x4_SRGB":
				    return VkFormat.Astc4X4SrgbBlock;
			    case "RenderFormat_ASTC_5x4_UNORM":
				    return VkFormat.Astc5X4UnormBlock;
			    case "RenderFormat_ASTC_5x4_SRGB":
				    return VkFormat.Astc5X4SrgbBlock;
			    case "RenderFormat_ASTC_5x5_UNORM":
				    return VkFormat.Astc5X5UnormBlock;
			    case "RenderFormat_ASTC_5x5_SRGB":
				    return VkFormat.Astc5X5SrgbBlock;
			    case "RenderFormat_ASTC_6x5_UNORM":
				    return VkFormat.Astc6X5UnormBlock;
			    case "RenderFormat_ASTC_6x5_SRGB":
				    return VkFormat.Astc6X5SrgbBlock;
			    case "RenderFormat_ASTC_6x6_UNORM":
				    return VkFormat.Astc6X6UnormBlock;
			    case "RenderFormat_ASTC_6x6_SRGB":
				    return VkFormat.Astc6X6SrgbBlock;
			    case "RenderFormat_ASTC_8x5_UNORM":
				    return VkFormat.Astc8X5UnormBlock;
			    case "RenderFormat_ASTC_8x5_SRGB":
				    return VkFormat.Astc8X5SrgbBlock;
			    case "RenderFormat_ASTC_8x6_UNORM":
				    return VkFormat.Astc8X6UnormBlock;
			    case "RenderFormat_ASTC_8x6_SRGB":
				    return VkFormat.Astc8X6SrgbBlock;
			    case "RenderFormat_ASTC_8x8_UNORM":
				    return VkFormat.Astc8X8UnormBlock;
			    case "RenderFormat_ASTC_8x8_SRGB":
				    return VkFormat.Astc8X8SrgbBlock;
			    case "RenderFormat_ASTC_10x5_UNORM":
				    return VkFormat.Astc10X5UnormBlock;
			    case "RenderFormat_ASTC_10x5_SRGB":
				    return VkFormat.Astc10X5SrgbBlock;
			    case "RenderFormat_ASTC_10x6_UNORM":
				    return VkFormat.Astc10X6UnormBlock;
			    case "RenderFormat_ASTC_10x6_SRGB":
				    return VkFormat.Astc10X6SrgbBlock;
			    case "RenderFormat_ASTC_10x8_UNORM":
				    return VkFormat.Astc10X8UnormBlock;
			    case "RenderFormat_ASTC_10x8_SRGB":
				    return VkFormat.Astc10X8SrgbBlock;
			    case "RenderFormat_ASTC_10x10_UNORM":
				    return VkFormat.Astc10X10UnormBlock;
			    case "RenderFormat_ASTC_10x10_SRGB":
				    return VkFormat.Astc10X10SrgbBlock;
			    case "RenderFormat_ASTC_12x10_UNORM":
				    return VkFormat.Astc12X10UnormBlock;
			    case "RenderFormat_ASTC_12x10_SRGB":
				    return VkFormat.Astc12X10SrgbBlock;
			    case "RenderFormat_ASTC_12x12_UNORM":
				    return VkFormat.Astc12X12UnormBlock;
			    case "RenderFormat_ASTC_12x12_SRGB":
				    return VkFormat.Astc12X12SrgbBlock;
			    case "RenderFormat_D24_UNORM_S8_UINT":
				    return VkFormat.D24UnormS8Uint;
			    case "RenderFormat_D32_FLOAT_S8_UINT":
				    return VkFormat.D32SfloatS8Uint;
			    case "RenderFormat_D16_UNORM":
				    return VkFormat.D16Unorm;
			    case "RenderFormat_D32_FLOAT":
				    return VkFormat.D32Sfloat;
			    default:
				    FrostyLogger.Logger?.LogError("{} is not supported by DXGI_FORMAT", inRenderFormat);
				    return VkFormat.Undefined;
			}
		}
	}