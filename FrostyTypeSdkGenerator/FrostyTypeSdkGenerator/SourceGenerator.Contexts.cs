using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace FrostyTypeSdkGenerator;

public sealed partial class SourceGenerator
{
    private record TypeContext(string? Namespace, string Name, bool IsValueType, ImmutableArray<MemberContext> Fields, ImmutableArray<MemberContext> Properties, TypeContext? ContainingType);

    private readonly record struct MemberContext(string Name, string Type, ImmutableArray<string> Attributes);

    private sealed class TypeContextEqualityComparer : IEqualityComparer<TypeContext>
    {
        private TypeContextEqualityComparer() { }

        public static TypeContextEqualityComparer Instance { get; } = new();

        public bool Equals(TypeContext x, TypeContext y)
        {
            return x.Namespace == y.Namespace &&
                   x.Name == y.Name &&
                   x.IsValueType == y.IsValueType &&
                   x.Fields.SequenceEqual(y.Fields, FieldContextEqualityComparer.Instance);
        }

        public int GetHashCode(TypeContext obj)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class FieldContextEqualityComparer : IEqualityComparer<MemberContext>
    {
        private FieldContextEqualityComparer() { }

        public static FieldContextEqualityComparer Instance { get; } = new();

        public bool Equals(MemberContext x, MemberContext y)
        {
            return x.Name == y.Name &&
                   x.Type == y.Type &&
                   x.Attributes.SequenceEqual(y.Attributes);
        }

        public int GetHashCode(MemberContext obj)
        {
            throw new NotImplementedException();
        }
    }
}