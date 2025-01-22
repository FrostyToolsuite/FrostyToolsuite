using System;
using System.Reflection;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.Interfaces;
using Frosty.Sdk.Sdk;

namespace Frosty.Sdk;

public class SdkType : IType
{
    public string Name => Type.GetName();
    public uint NameHash => Type.GetNameHash();
    public Guid Guid => Type.GetGuid();
    public uint Signature => Type.GetSignature();

    public Type Type { get; }

    private static readonly string s_collectionName = "ObservableCollection`1";

    public SdkType(Type inType)
    {
        Type = inType;
    }

    public bool IsSubClassOf(IType inType)
    {
        return Type.IsSubclassOf(inType.Type);
    }

    public TypeFlags GetFlags()
    {
        Type type = Type;
        if (Type.Name == s_collectionName)
        {
            type = Type.GenericTypeArguments[0];
        }
        TypeFlags? flags = type.GetCustomAttribute<EbxTypeMetaAttribute>()?.Flags;
        if (!flags.HasValue)
        {
            // should only be PointerRef for writing boxed class values
            return new TypeFlags(TypeFlags.TypeEnum.Class, TypeFlags.CategoryEnum.Class);
        }
        return new TypeFlags(flags.Value.GetTypeEnum(), flags.Value.GetCategoryEnum());
    }
}