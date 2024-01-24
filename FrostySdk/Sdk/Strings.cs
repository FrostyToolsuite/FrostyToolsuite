using System.Collections.Generic;

namespace Frosty.Sdk.Sdk;

internal static class Strings
{
    public static bool HasStrings;

    public static HashSet<string> TypeNames = new();

    public static Dictionary<string, HashSet<string>> FieldNames = new();

    public static HashSet<uint> TypeHashes = new();

    public static Dictionary<uint, HashSet<uint>> FieldHashes = new();

    public static Dictionary<uint, string> TypeMapping = new();

    public static Dictionary<uint, Dictionary<uint, string>> FieldMapping = new();
}