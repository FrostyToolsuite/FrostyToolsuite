using System.Text.Json;
using Frosty.Sdk.IO;

namespace Frosty.ModSupport.Mod;

public class FrostyModCollection
{
    public class Manifest
    {
        public string Link { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public List<string> Mods { get; set; } = new();
        public List<string> ModVersions { get; set; } = new();
    }

    public IEnumerable<FrostyMod> Mods => m_mods;
    
    /// <summary>
    /// 'FCOL'
    /// </summary>
    private static readonly uint s_magic = 0x46434F4C;
    private static readonly uint s_version = 1;

    private FrostyModDetails m_modDetails;
    private ResourceData m_icon;
    private ResourceData[] m_screenshots;
    private FrostyMod[] m_mods;

    private FrostyModCollection(FrostyModDetails inModDetails, ResourceData inIcon, ResourceData[] inScreenshots,
        FrostyMod[] inMods)
    {
        m_modDetails = inModDetails;
        m_icon = inIcon;
        m_screenshots = inScreenshots;
        m_mods = inMods;
    }
    
    public static FrostyModCollection? Load(string inPath)
    {
        if (!File.Exists(inPath))
        {
            return null;
        }
        
        using (BlockStream stream = BlockStream.FromFile(inPath, false))
        {
            if (s_magic != stream.ReadUInt32())
            {
                return null;
            }

            if (s_version != stream.ReadUInt32())
            {
                return null;
            }
            
            uint manifestOffset = stream.ReadUInt32();
            int manifestSize = stream.ReadInt32();
            ResourceData icon = new(inPath, stream.ReadUInt32(), stream.ReadInt32());
            uint screenShotsOffset = stream.ReadUInt32();

            stream.Position = manifestOffset;
            Span<byte> utf8Json = new byte[manifestSize];
            stream.ReadExactly(utf8Json);
            Manifest? manifest = JsonSerializer.Deserialize<Manifest>(utf8Json);
            if (manifest is null)
            {
                // should never happen, since invalid or corrupted mods should be caught with magic and version
                return null;
            }

            FrostyModDetails modDetails = new(manifest.Title, manifest.Author, manifest.Category, manifest.Version,
                manifest.Description, manifest.Link);

            stream.Position = screenShotsOffset;
            int count = stream.ReadInt32();
            ResourceData[] screenshots = new ResourceData[count];
            long offset = screenShotsOffset + 4 + 4;
            for (int i = 0; i < count; i++)
            {
                int size = stream.ReadInt32();
                screenshots[i] = new ResourceData(inPath, offset, size);
                offset += size + 4;
            }

            FrostyMod[] mods = new FrostyMod[manifest.Mods.Count];
            for (int i = 0; i < mods.Length; i++)
            {
                FrostyMod? mod = FrostyMod.Load(manifest.Mods[i]);
                if (mod is null)
                {
                    return null;
                }

                mods[i] = mod;
            }

            return new FrostyModCollection(modDetails, icon, screenshots, mods);
        }
    }
    
    public static FrostyModDetails? GetModDetails(string inPath)
    {
        using (BlockStream stream = BlockStream.FromFile(inPath, false))
        {
            if (s_magic != stream.ReadUInt32())
            {
                return null;
            }

            if (s_version != stream.ReadUInt32())
            {
                return null;
            }

            uint manifestOffset = stream.ReadUInt32();
            int manifestSize = stream.ReadInt32();
            
            stream.Position = manifestOffset;
            Span<byte> utf8Json = new byte[manifestSize];
            stream.ReadExactly(utf8Json);
            Manifest? manifest = JsonSerializer.Deserialize<Manifest>(utf8Json);

            if (manifest is null)
            {
                return null;
            }
            
            return new FrostyModDetails(manifest.Title, manifest.Author, manifest.Category,
                manifest.Version, manifest.Description, manifest.Link);
        }
    }
}