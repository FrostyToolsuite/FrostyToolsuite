using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace FrostyCli;

public static class AnthemDemo
{
    public static void ConvertOldStringsFile(string inFile = "AnthemDemo-Strings.txt", string outPath = "Sdk/Strings")
    {
        HashSet<string> typeNames = new();
        Dictionary<string, HashSet<string>> fieldNames = new();

        using (TextReader reader = new StreamReader(inFile))
        {
            int count = int.Parse(reader.ReadLine()!);
            for (int i = 0; i < count; i++)
            {
                reader.ReadLine();
            }

            Dictionary<uint, string> map = new();
            count = int.Parse(reader.ReadLine()!);
            for (int i = 0; i < count; i++)
            {
                string line = reader.ReadLine()!;
                string[] arr = line.Split(',');
                uint hash = uint.Parse(arr[0]);

                typeNames.Add(arr[1]);
                map.Add(hash, arr[1]);
            }

            count = int.Parse(reader.ReadLine()!);
            for (int i = 0; i < count; i++)
            {
                uint hash = uint.Parse(reader.ReadLine()!);
                int fieldCount = int.Parse(reader.ReadLine()!);

                if (hash == 24714)
                {
                    for (int j = 0; j < fieldCount; j++)
                    {
                        // enum fields had the flags instead of the type hash at the start
                        string line = reader.ReadLine()!;
                        string[] arr = line.Split(',');

                        string[] arr2 = arr[1].Split('_');

                        if (arr2[0].StartsWith("EIA"))
                        {
                            arr2[0] = "EntryInputAction";
                        }
                        else if (arr2[0].StartsWith("IDME"))
                        {
                            arr2[0] = "InputDeviceMessageEvent";
                        }
                        else if (arr2[0].StartsWith("MCPPT"))
                        {
                            arr2[0] = "MeshComputeParameterPayloadType";
                        }
                        else if (arr2[0] == "DylanOption")
                        {
                            arr2[0] = "BWOptionsOptionId";
                        }
                        else if (arr2.Length < 2)
                        {
                            if (arr2[0] == "MeshComputeOutputTypeCount")
                            {
                                arr2[0] = "MeshComputeOutputType";
                            }
                            else if (arr2[0].StartsWith("Concept"))
                            {
                                arr2[0] = "InputConceptIdentifier";
                            }
                            else if (arr2[0].StartsWith("Route"))
                            {
                                arr2[0] = "RouteType";
                            }
                            else
                            {
                                Logger.LogErrorInternal(arr2[0]);
                                continue;
                            }
                        }

                        Debug.Assert(typeNames.Contains(arr2[0]));

                        fieldNames.TryAdd(arr2[0], new HashSet<string>());
                        fieldNames[arr2[0]].Add(arr[1]);
                    }
                }
                else
                {
                    fieldNames.Add(map[hash], new HashSet<string>());
                    for (int j = 0; j < fieldCount; j++)
                    {
                        string line = reader.ReadLine()!;
                        string[] arr = line.Split(',');
                        fieldNames[map[hash]].Add(arr[1]);
                    }
                }
            }
        }

        File.WriteAllText(Path.Combine(outPath, "anthem_demo_types.json"), JsonSerializer.Serialize(typeNames));
        File.WriteAllText(Path.Combine(outPath, "anthem_demo_fields.json"), JsonSerializer.Serialize(fieldNames));
    }
}