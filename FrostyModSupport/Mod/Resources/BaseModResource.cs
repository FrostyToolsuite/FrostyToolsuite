using Frosty.ModSupport.Interfaces;
using Frosty.Sdk;
using Frosty.Sdk.IO;

namespace Frosty.ModSupport.Mod.Resources;

public abstract class BaseModResource
{
    [Flags]
    public enum ResourceFlags : byte
    {
        IsAdded = 1 << 3
    }
    
    /// <summary>
    /// The <see cref="ModResourceType"/> of this <see cref="BaseModResource"/>.
    /// </summary>
    public virtual ModResourceType Type => ModResourceType.Invalid;

    /// <summary>
    /// The index into the data array of the <see cref="IResourceContainer"/> or -1 if it doesn't have any data.
    /// </summary>
    public int ResourceIndex { get; }
    
    /// <summary>
    /// The name of this <see cref="BaseModResource"/>.
    /// </summary>
    public string Name { get; }
    
    /// <summary>
    /// The <see cref="Sha1"/> hash of the data of this <see cref="BaseModResource"/>.
    /// </summary>
    public Sha1 Sha1 { get; }
    
    /// <summary>
    /// The uncompressed size of the data of this <see cref="BaseModResource"/>.
    /// </summary>
    public long OriginalSize { get; }
    
    /// <summary>
    /// The hash of the handler if this <see cref="BaseModResource"/> has one, else its 0.
    /// </summary>
    public int HandlerHash { get; }

    /// <summary>
    /// The user data of this <see cref="BaseModResource"/>.
    /// </summary>
    public string UserData { get; } = string.Empty;

    /// <summary>
    /// Indicates if this <see cref="BaseModResource"/> has data.
    /// </summary>
    public bool IsModified => ResourceIndex != -1 && Type != ModResourceType.Embedded && Type != ModResourceType.Bundle;
    
    /// <summary>
    /// The <see cref="ResourceFlags"/> of this <see cref="BaseModResource"/>.
    /// </summary>
    public ResourceFlags Flags { get; }
    
    /// <summary>
    /// Indicates if this <see cref="BaseModResource"/> has a handler.
    /// </summary>
    public bool HasHandler => HandlerHash != 0;

    /// <summary>
    /// The bundles this <see cref="BaseModResource"/> is added to.
    /// </summary>
    public IEnumerable<int> AddedBundles => m_bundlesToAdd;

    /// <summary>
    /// The bundles this <see cref="BaseModResource"/> is removed from.
    /// </summary>
    public IEnumerable<int> RemovedBundles => m_bundlesToRemove;
    
    private readonly HashSet<int> m_bundlesToAdd = new();
    private readonly HashSet<int> m_bundlesToRemove = new();

    protected BaseModResource(DataStream inStream)
    {
        ResourceIndex = inStream.ReadInt32();
        Name = inStream.ReadNullTerminatedString();

        if (ResourceIndex != -1)
        {
            Sha1 = inStream.ReadSha1();
            OriginalSize = inStream.ReadInt64();
            Flags = (ResourceFlags)inStream.ReadByte();
            HandlerHash = inStream.ReadInt32();
            UserData = inStream.ReadNullTerminatedString();
        }
        
        int addCount = inStream.ReadInt32();
        for (int i = 0; i < addCount; i++)
        {
            m_bundlesToAdd.Add(inStream.ReadInt32());
        }
        
        int removeCount = inStream.ReadInt32();
        for (int i = 0; i < removeCount; i++)
        {
            m_bundlesToRemove.Add(inStream.ReadInt32());
        }
    }

    protected BaseModResource(int inResourceIndex, string inName, Sha1 inSha1, long inOriginalSize,
        ResourceFlags inFlags, int inHandlerHash, string inUserData, IEnumerable<int> inBundlesToAdd,
        IEnumerable<int> inBundlesToRemove)
    {
        ResourceIndex = inResourceIndex;
        Name = inName;
        Sha1 = inSha1;
        OriginalSize = inOriginalSize;
        Flags = inFlags;
        HandlerHash = inHandlerHash;
        UserData = inUserData;
        m_bundlesToAdd.UnionWith(inBundlesToAdd);
        m_bundlesToRemove.UnionWith(inBundlesToRemove);
    }

    public virtual void Write(DataStream stream)
    {
        stream.WriteByte((byte)Type);
        stream.WriteInt32(ResourceIndex);
        stream.WriteNullTerminatedString(Name);

        if (ResourceIndex != -1)
        {
            stream.WriteSha1(Sha1);
            stream.WriteInt64(OriginalSize);
            stream.WriteByte((byte)Flags);
            stream.WriteInt32(HandlerHash);
            stream.WriteNullTerminatedString(UserData);
        }
        
        stream.WriteInt32(m_bundlesToAdd.Count);
        foreach (int bundleId in m_bundlesToAdd)
        {
            stream.WriteInt32(bundleId);
        }
        
        stream.WriteInt32(m_bundlesToRemove.Count);
        foreach (int bundleId in m_bundlesToRemove)
        {
            stream.WriteInt32(bundleId);
        }
    }
}