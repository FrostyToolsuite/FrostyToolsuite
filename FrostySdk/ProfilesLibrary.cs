using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Frosty.Sdk.Profiles;
using Frosty.Sdk.IO.Compression;

namespace Frosty.Sdk;

public static class ProfilesLibrary
{
    public static bool IsInitialized { get; private set; }

    public static string ProfileName => s_effectiveProfile?.Name ?? string.Empty;
    public static string DisplayName => s_effectiveProfile?.DisplayName ?? string.Empty;
    public static string InternalName => s_effectiveProfile?.InternalName?? string.Empty;
    public static string TypeInfoSignature => s_effectiveProfile?.TypeInfoSignature ?? string.Empty;
    public static bool HasStrippedTypeNames => s_effectiveProfile?.HasStrippedTypeNames ?? false;
    public static string TypeHashSeed => s_effectiveProfile?.TypeHashSeed ?? string.Empty;
    public static int DataVersion => s_effectiveProfile?.DataVersion ?? -1;
    public static FrostbiteVersion FrostbiteVersion => s_effectiveProfile?.FrostbiteVersion ?? "0.0.0";
    public static string SdkPath => s_effectiveProfile is null ? string.Empty : Path.Combine(Utils.Utils.BaseDirectory, "Sdk", $"{s_effectiveProfile.InternalName}.dll");

    public static int EbxVersion => s_effectiveProfile?.EbxVersion ?? -1;
    public static bool RequiresInitFsKey => s_effectiveProfile?.RequiresInitFsKey ?? false;
    public static bool RequiresBundleKey => s_effectiveProfile?.RequiresBundleKey ?? false;
    public static bool RequiresCasKey => s_effectiveProfile?.RequiresBundleKey ?? false;
    public static bool MustAddChunks => s_effectiveProfile?.MustAddChunks ?? false;
    public static bool EnableExecution => s_effectiveProfile?.EnableExecution ?? false;
    public static bool HasAntiCheat => s_effectiveProfile?.HasAntiCheat ?? false;

    public static CompressionType EbxCompression => (CompressionType)(s_effectiveProfile?.EbxCompression ?? 0);
    public static CompressionType ResCompression => (CompressionType)(s_effectiveProfile?.ResCompression ?? 0);
    public static CompressionType ChunkCompression => (CompressionType)(s_effectiveProfile?.ChunkCompression ?? 0);
    public static CompressionType TextureChunkCompression => (CompressionType)(s_effectiveProfile?.TextureChunkCompression ?? 0);
    public static int MaxBufferSize => s_effectiveProfile?.MaxBufferSize ?? 0;
    public static int ZStdCompressionLevel => s_effectiveProfile?.ZStdCompressionLevel ?? 0;

    public static string DefaultDiffuse => s_effectiveProfile?.DefaultDiffuse ?? string.Empty;
    public static string DefaultNormals => s_effectiveProfile?.DefaultNormals ?? string.Empty;
    public static string DefaultMask => s_effectiveProfile?.DefaultMask ?? string.Empty;
    public static string DefaultTint => s_effectiveProfile?.DefaultTint ?? string.Empty;

    public static bool HasLoadedProfile => s_effectiveProfile is not null;

    public static readonly Dictionary<int, string> SharedBundles = new();

    private static Profile? s_effectiveProfile;
    private static bool s_profilesLoaded;
    private static readonly List<Profile> s_profiles = new();

    public static void Initialize()
    {
        string profilesPath = Path.Combine(Utils.Utils.BaseDirectory, "Profiles");
        if (Directory.Exists(profilesPath))
        {
            foreach (string file in Directory.EnumerateFiles(profilesPath))
            {
                Profile? profile;
                using (FileStream stream = new(file, FileMode.Open, FileAccess.Read))
                {
                    profile = JsonSerializer.Deserialize<Profile>(stream);
                }
                if (profile is not null)
                {
                    s_profiles.Add(profile);
                }
            }
        }

        s_profilesLoaded = true;
    }

    public static bool Initialize(string profileKey)
    {
        if (IsInitialized)
        {
            return true;
        }
        if (!s_profilesLoaded)
        {
            Initialize();
        }
        s_effectiveProfile = s_profiles.Find(a => a.Name.Equals(profileKey, StringComparison.OrdinalIgnoreCase));
        if (s_effectiveProfile is not null)
        {
            foreach (string bundle in  s_effectiveProfile.SharedBundles)
            {
                SharedBundles.Add(Utils.Utils.HashString(bundle, true), bundle);
            }

            FrostyLogger.Logger?.LogInfo($"Loading profile {s_effectiveProfile.DisplayName}");

            IsInitialized = true;
            return true;
        }

        FrostyLogger.Logger?.LogError($"No profile found in directory {Path.Combine(Utils.Utils.BaseDirectory, "Profiles")} for key {profileKey}");
        return false;
    }

    public static bool HasProfile(string profileKey)
    {
        return s_profiles.FindIndex(a => a.Name.Equals(profileKey, StringComparison.OrdinalIgnoreCase)) != -1;
    }

    public static bool IsLoaded(ProfileVersion version)
    {
        return version == (ProfileVersion)DataVersion;
    }

    public static bool IsLoaded(params ProfileVersion[] versions)
    {
        return versions.Contains((ProfileVersion)DataVersion);
    }

    public static string? GetDisplayName(string profileKey)
    {
        return s_profiles.Find(a => a.Name.Equals(profileKey, StringComparison.OrdinalIgnoreCase))?.DisplayName;
    }
}