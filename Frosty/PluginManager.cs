using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Frosty.Sdk;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.Interfaces;

namespace Frosty;

public static class PluginManager
{
    public static Dictionary<string, ExportEbxDelegate> EbxExportDelegates = new();

    public static void LoadPlugins(string inPath)
    {
        foreach (string file in Directory.EnumerateFiles(inPath, "*.dll", SearchOption.AllDirectories))
        {
            Assembly assembly = Assembly.LoadFrom(file);

            foreach (Type type in assembly.GetExportedTypes())
            {
                if (type.GetCustomAttribute<FrostyPluginAttribute>() is null)
                {
                    continue;
                }
                MethodInfo[] methods = type.GetMethods();

                foreach (MethodInfo method in methods)
                {
                    ExportEbxFunctionAttribute? attr = method.GetCustomAttribute<ExportEbxFunctionAttribute>();
                    if (attr is null)
                    {
                        continue;
                    }
                    ExportEbxDelegate export = method.CreateDelegate<ExportEbxDelegate>();
                    foreach (IType t in TypeLibrary.EnumerateTypes())
                    {
                        if (!TypeLibrary.IsSubClassOf(t, attr.Type))
                        {
                            continue;
                        }
                        EbxExportDelegates.Add(t.Name, export);
                    }
                }
            }
        }
    }
}