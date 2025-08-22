using System;
using System.Collections.Generic;
using System.Linq;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.Managers.Infos.FileInfos;

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

    internal IFileInfo? FileInfo => m_fileInfo;

    private IFileInfo? m_fileInfo;

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

    internal void AddFileInfo(IFileInfo? inFileInfo)
    {
        if (inFileInfo is null)
        {
            return;
        }

        if (!inFileInfo.FileExists())
        {
            return;
        }

        if (m_fileInfo is null)
        {
            m_fileInfo = inFileInfo;
            return;
        }

        if (!m_fileInfo.IsComplete() && inFileInfo.IsComplete())
        {
            m_fileInfo = inFileInfo;
            return;
        }

        if (m_fileInfo.IsComplete() && !inFileInfo.IsComplete())
        {
            return;
        }

        if (inFileInfo is CasFileInfo && m_fileInfo is not CasFileInfo)
        {
            m_fileInfo = inFileInfo;
            return;
        }

        if (m_fileInfo is CasFileInfo && inFileInfo is not CasFileInfo)
        {
            return;
        }

        if (m_fileInfo.IsDelta() && !inFileInfo.IsDelta())
        {
            m_fileInfo = inFileInfo;
        }
    }
}