using System.Collections.Generic;

namespace Frosty.Sdk.Sdk;

internal static class Strings
{
    public static bool HasStrings;

    public static HashSet<string>? TypeNames;

    public static Dictionary<string, HashSet<string>>? FieldNames;

    public static HashSet<uint>? TypeHashes;

    public static Dictionary<uint, HashSet<uint>>? FieldHashes;

    public static Dictionary<uint, string>? TypeMapping;

    public static Dictionary<uint, Dictionary<uint, string>>? FieldMapping;

    public static void Reset()
    {
        HasStrings = false;
        TypeNames = null;
        FieldNames = null;
        TypeHashes = null;
        FieldHashes = null;
        TypeMapping = null;
        FieldMapping = null;
    }
}