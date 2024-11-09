using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Frosty.Sdk;
using Frosty.Sdk.Attributes;
using Frosty.Sdk.Ebx;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Utils;
using Microsoft.Extensions.Logging;

namespace Frosty;

internal static partial class Program
{
	private static void AddCommands(RootCommand inRoot)
	{
        // -- QUIT --
		Command command = new("quit", "Quit the program");
		command.AddAlias("q");
		command.SetHandler(HandleQuit);
		inRoot.AddCommand(command);

        // -- LOAD --
		Argument<string> gamePath = new(name:"game-path", description:"The path to the Frostbite game executable to load", parse: result =>
		{
			if (result.Tokens.Count > 1)
			{
				result.ErrorMessage = "Only one game path can be specified";
				return string.Empty;
			}
			string value = result.Tokens[0].Value;
			if (!File.Exists(value))
			{
				result.ErrorMessage = "The game executable '" + value + "' does not exist";
				return string.Empty;
			}
			return value;
		});
		Option<int> pid = new("--pid", () => -1, "The process id of the running game, needed if a type sdk needs to be generated from the game");
		Command load = new("load", "Loads a Frostbite game") { gamePath, s_initFsKey, s_bundleKey, s_casKey, pid };
		load.AddAlias("l");
		load.SetHandler((a, b) =>
		{
			s_gameIsLoaded = LoadGame(a, b);
		}, gamePath, pid);
		inRoot.AddCommand(load);

        // -- DEBUG --
        AddDebugCommands(inRoot);

        // -- STUFF --
		AddExportCommand(inRoot);
		AddSearchCommand(inRoot);
	}

    private static void AddDebugCommands(RootCommand inRoot)
    {
        Command resTypes = new("res-types") { IsHidden = true };
        resTypes.SetHandler(() =>
        {
            Dictionary<uint, List<string>> types = new();
            foreach (Type type in TypeLibrary.EnumerateTypes())
            {
                string name = type.GetName();
                uint hash = (uint)Utils.HashString(name, true);
                if (!types.TryAdd(hash, [name]))
                {
                    types[hash].Add(name);
                }
            }

            StringBuilder sb = new();
            HashSet<uint> resolved = new();
            foreach (ResAssetEntry entry in AssetManager.EnumerateResAssetEntries())
            {
                if (!Enum.IsDefined(entry.ResType))
                {
                    if (!resolved.Add((uint)entry.ResType))
                    {
                        continue;
                    }

                    if (!types.ContainsKey((uint)entry.ResType))
                    {
                        FrostyLogger.Logger?.LogError("Could not resolve ResType: {} ({})", (uint)entry.ResType, entry.Name);
                        continue;
                    }

                    List<string> v = types[(uint)entry.ResType];
                    if (v.Count > 1)
                    {
                        string names = v[0];
                        for (int i = 1; i < v.Count; i++)
                        {
                            names += ", " + v[i];
                        }

                        FrostyLogger.Logger?.LogError("Duplicate hash for ResType: {}", names);
                        continue;
                    }
                    sb.AppendLine($"{v[0]} = 0x{(uint)entry.ResType:X8},");
                }
            }
            Console.Write(sb.ToString());
        });
        inRoot.AddCommand(resTypes);

        Command info = new("info") { IsHidden = true };
        info.AddAlias("i");

        Argument<string> name = new("name", "The name of the ebx to export, can include wildcards '?' and '*'");
        Command res = new("res") { name };
        res.SetHandler(inName =>
        {
            ResAssetEntry? entry = AssetManager.GetResAssetEntry(inName);
            if (entry is null)
            {
                FrostyLogger.Logger?.LogError("Res with the name: \"{}\" does not exist", inName);
                return;
            }
            Console.WriteLine($"ResType: {entry.ResType}");
            char[] meta = new char[entry.ResMeta.Length * 2];
            for (int i = 0; i < entry.ResMeta.Length; i++)
            {
                string hex = entry.ResMeta[i].ToString("X2");
                meta[i * 2] = hex[0];
                meta[i * 2 + 1] = hex[1];
            }
            Console.WriteLine($"ResMeta: {new string(meta)}");
            Console.WriteLine($"ResRid: {entry.ResRid}");
        }, name);
        info.AddCommand(res);
        inRoot.AddCommand(info);

        Command usedTypes = new("used-types") { IsHidden = true };
        res = new("res");
        res.SetHandler(() =>
        {
            HashSet<uint> used = new();
            foreach (ResAssetEntry entry in AssetManager.EnumerateResAssetEntries())
            {
                if (used.Add((uint)entry.ResType))
                {
                    Console.WriteLine(entry.ResType);
                }
            }
        });
        usedTypes.AddCommand(res);
        inRoot.AddCommand(usedTypes);
    }

	private static void AddExportCommand(RootCommand inRoot)
	{
		Command export = new("export");
		export.AddAlias("e");

        Argument<string> name = new("name", "The name of the asset to export, can include wildcards '?' and '*'");
        Option<bool> preserveStructure = new("--preserve-structure", "Preserves the folder structure of the asset");
		Option<string> type = new("--type", "The allowed types of the asset to export");
        Option<string> output = new("--output", () => string.Empty, "The folder in which the ebx will be exported");
		output.AddAlias("-o");

        Option<bool> convert = new("--convert",
            "Convert the ebx to a readable format (dbx) and if possible to a common format for e.g. textures, meshes, sounds, etc. ");
        Command ebx = new("ebx") { name, convert, preserveStructure, type, output };
		ebx.SetHandler(HandleEbxExport, name, convert, preserveStructure, type, output);
        export.AddCommand(ebx);

        Option<bool> resMeta = new("--res-meta", "Inserts the ResMeta at the start of the data");
        Command res = new("res") { name, resMeta, preserveStructure, type, output };
        res.SetHandler(HandleResExport, name, resMeta, preserveStructure, type, output);
        export.AddCommand(res);

        Command chunk = new("chunk") { name, output };
        chunk.SetHandler(HandleChunkExport, name, output);
        export.AddCommand(chunk);

		inRoot.AddCommand(export);
	}

	private static void AddSearchCommand(RootCommand inRoot)
	{
		Command search = new("search", "Search for an asset in the virtual filesystem of the loaded game");
		search.AddAlias("s");

        Argument<string> name = new("name", "The name to search for, can include wildcards '?' and '*'");
		Option<string> type = new("--type", "The type of the ebx to filter");
		Command ebx = new("ebx") { name, type };
		ebx.SetHandler(HandleEbxSearch, name, type);
		search.AddCommand(ebx);

        type = new Option<string>("--type", "The type of the res to filter");
        Command res = new("res") { name, type };
        res.SetHandler(HandleResSearch, name, type);
        search.AddCommand(res);

        Command chunk = new("chunk") { name };
        chunk.SetHandler(HandleChunkSearch, name);
        search.AddCommand(chunk);

		inRoot.AddCommand(search);
	}

	private static void HandleEbxSearch(string inName, string? inType)
	{
		string pattern = "^" + Regex.Escape(inName).Replace("\\?", ".").Replace("\\*", ".*") + "$";
		foreach (EbxAssetEntry item in AssetManager.EnumerateEbxAssetEntries())
		{
			if (Regex.IsMatch(item.Name, pattern) && (string.IsNullOrEmpty(inType) || TypeLibrary.IsSubClassOf(item.Type, inType)))
			{
				Console.WriteLine(item.Name);
			}
		}
	}

    private static void HandleResSearch(string inName, string? inType)
    {
        string pattern = "^" + Regex.Escape(inName).Replace("\\?", ".").Replace("\\*", ".*") + "$";
        foreach (ResAssetEntry item in AssetManager.EnumerateResAssetEntries())
        {
            if (Regex.IsMatch(item.Name, pattern) && (string.IsNullOrEmpty(inType) || item.ResType.ToString() == inType))
            {
                Console.WriteLine(item.Name);
            }
        }
    }

    private static void HandleChunkSearch(string inName)
    {
        string pattern = "^" + Regex.Escape(inName).Replace("\\?", ".").Replace("\\*", ".*") + "$";
        foreach (ChunkAssetEntry item in AssetManager.EnumerateChunkAssetEntries())
        {
            if (Regex.IsMatch(item.Name, pattern))
            {
                Console.WriteLine(item.Name);
            }
        }
    }

	private static void HandleEbxExport(string inName, bool inConvert, bool inPreserveStructure, string? inType, string inOutput)
	{
		EbxAssetEntry? entry = AssetManager.GetEbxAssetEntry(inName);
		if (entry is null)
		{
			string pattern = "^" + Regex.Escape(inName).Replace("\\?", ".").Replace("\\*", ".*") + "$";
			foreach (EbxAssetEntry item in AssetManager.EnumerateEbxAssetEntries())
            {
                if (Regex.IsMatch(item.Name, pattern) && (string.IsNullOrEmpty(inType) || TypeLibrary.IsSubClassOf(item.Type, inType)))
                {
                    ExportEbx(item, inConvert, Path.Combine(inOutput, inPreserveStructure ? item.Name : item.Filename));
                }
            }
            return;
		}
		ExportEbx(entry, inConvert, Path.Combine(inOutput, inPreserveStructure ? entry.Name : entry.Filename));
	}

	private static void ExportEbx(EbxAssetEntry inEntry, bool inConvert, string inPath)
	{
		FileInfo fileInfo = new(inPath);
		fileInfo.Directory?.Create();
		if (inConvert)
		{
			EbxAsset asset = AssetManager.GetEbxAsset(inEntry);

            if (PluginManager.EbxExportDelegates.TryGetValue(inEntry.Type, out ExportEbxDelegate? export))
            {
                export(asset, inPath);
            }

			using DbxWriter dbxWriter = new(fileInfo.FullName + ".dbx");
			dbxWriter.Write(asset);
			return;
		}
		using Block<byte> block = AssetManager.GetAsset(inEntry);
		File.WriteAllBytes(fileInfo.FullName + ".ebx", block.ToArray());
	}

    private static void HandleResExport(string inName, bool inResMeta, bool inPreserveStructure, string? inType, string inOutput)
    {
        ResAssetEntry? entry = AssetManager.GetResAssetEntry(inName);
        if (entry is null)
        {
            string pattern = "^" + Regex.Escape(inName).Replace("\\?", ".").Replace("\\*", ".*") + "$";
            foreach (ResAssetEntry item in AssetManager.EnumerateResAssetEntries())
            {
                if (Regex.IsMatch(item.Name, pattern) && (string.IsNullOrEmpty(inType) || item.ResType.ToString() == inType))
                {
                    ExportRes(item, inResMeta, Path.Combine(inOutput, inPreserveStructure ? item.Name : item.Filename));
                }
            }
            return;
        }
        ExportRes(entry, inResMeta, Path.Combine(inOutput, inPreserveStructure ? entry.Name : entry.Filename));
    }

    private static void ExportRes(ResAssetEntry inEntry, bool inResMeta, string inPath)
    {
        FileInfo fileInfo = new(inPath);
        fileInfo.Directory?.Create();

        using Block<byte> block = AssetManager.GetAsset(inEntry);
        if (inResMeta)
        {
            using FileStream fileStream = File.Create(fileInfo.FullName + "." + inEntry.ResType);

            fileStream.Write(inEntry.ResMeta);
            fileStream.Write(block);

            return;
        }
        File.WriteAllBytes(fileInfo.FullName + "." + inEntry.ResType, block.ToArray());
    }

    private static void HandleChunkExport(string inName, string inOutput)
    {
        if (Guid.TryParse(inName, out Guid id))
        {
            ChunkAssetEntry? entry = AssetManager.GetChunkAssetEntry(id);
            if (entry is null)
            {
                FrostyLogger.Logger?.LogError("No chunk with id {} exists", id);
                return;
            }
            ExportChunk(entry, Path.Combine(inOutput, entry.Name));
        }

        string pattern = "^" + Regex.Escape(inName).Replace("\\?", ".").Replace("\\*", ".*") + "$";
        foreach (ChunkAssetEntry item in AssetManager.EnumerateChunkAssetEntries())
        {
            if (Regex.IsMatch(item.Name, pattern))
            {
                ExportChunk(item, Path.Combine(inOutput, item.Name));
            }
        }
    }

    private static void ExportChunk(ChunkAssetEntry inEntry, string inPath)
    {
        FileInfo fileInfo = new(inPath);
        fileInfo.Directory?.Create();

        using Block<byte> block = AssetManager.GetAsset(inEntry);
        File.WriteAllBytes(fileInfo.FullName + ".chunk", block.ToArray());
    }

	private static void HandleQuit()
	{
		Environment.Exit(0);
	}
}