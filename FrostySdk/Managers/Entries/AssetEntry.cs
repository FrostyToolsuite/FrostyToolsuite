using System;
using System.Collections.Generic;
using System.Linq;
using Frosty.Sdk.Interfaces;

namespace Frosty.Sdk.Managers.Entries;

public abstract class AssetEntry
{
    /// <summary>
    /// The Type of this <see cref="AssetEntry"/>.
    /// </summary>
    public virtual string Type { get; internal set; } = string.Empty;

    /// <summary>
    /// The AssetType of this <see cref="AssetEntry"/>.
    /// </summary>
    public virtual string AssetType => string.Empty;
    
    /// <summary>
    /// The Filename of this <see cref="AssetEntry"/>.
    /// </summary>
    public virtual string Filename
    {
        get
        {
            int id = Name.LastIndexOf('/');
            return id == -1 ? Name : Name[(id + 1)..];
        }
    }
    
    /// <summary>
    /// The Path of this <see cref="AssetEntry"/>.
    /// </summary>
    public virtual string Path
    {
        get
        {
            int id = Name.LastIndexOf('/');
            return id == -1 ? string.Empty : Name[..id];
        }
    }
    
    /// <summary>
    /// The name of this <see cref="AssetEntry"/>.
    /// </summary>
    public string Name { get; internal set; } = string.Empty;

    /// <summary>
    /// The <see cref="Sha1"/> hash of the compressed data of this <see cref="AssetEntry"/>.
    /// </summary>
    public Sha1 Sha1 { get; internal set; }

    /// <summary>
    /// The size of the uncompressed data of this <see cref="AssetEntry"/>.
    /// </summary>
    public long OriginalSize { get; internal set; }

    /// <summary>
    /// The Bundles that contain this <see cref="AssetEntry"/>. 
    /// </summary>
    public readonly HashSet<int> Bundles = new();

    internal IFileInfo FileInfo
    {
        get => m_fileInfo ??= GetDefaultFileInfo();
        set => m_fileInfo = value;
    }

    private IFileInfo? m_fileInfo;

    /// <summary>
    /// The <see cref="IFileInfo"/>s of this <see cref="AssetEntry"/>.
    /// </summary>
    internal readonly HashSet<IFileInfo> FileInfos = new();

    protected AssetEntry(Sha1 inSha1, long inOriginalSize)
    {
        Sha1 = inSha1;
        OriginalSize = inOriginalSize;
    }

    /// <summary>
    /// Checks if this <see cref="AssetEntry"/> is in the specified Bundle.
    /// </summary>
    /// <param name="bid">The Id of the Bundle.</param>
    /// <returns></returns>
    public bool IsInBundle(int bid) => Bundles.Contains(bid);

    /// <summary>
    /// Iterates through all bundles that the asset is a part of
    /// </summary>
    public IEnumerable<int> EnumerateBundles() => Bundles;

    private IFileInfo GetDefaultFileInfo()
    {
        if (FileInfos.Count == 0)
        {
            throw new Exception($"No found FileInfos for Asset: {Name}.");
        }
        
        IFileInfo? retVal = default;
        foreach (IFileInfo fileInfo in FileInfos)
        {
            if (fileInfo.IsComplete())
            {
                return fileInfo;
            }

            retVal ??= fileInfo;
        }

        if (retVal is null)
        {
            throw new Exception("we fucked up");
        }
        
        return retVal;
    }
}