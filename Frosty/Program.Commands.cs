using System;
using System.CommandLine;
using System.IO;
using System.Text.RegularExpressions;
using Frosty.Sdk;
using Frosty.Sdk.Ebx;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Utils;

namespace Frosty;

internal static partial class Program
{
	private static void AddCommands(RootCommand inRoot)
	{
		Command command = new("quit", "Quit the program");
		command.AddAlias("q");
		command.SetHandler(HandleQuit);
		inRoot.AddCommand(command);
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
		Command load = new("load", "Loads a Frostbite game") { gamePath, pid };
		load.AddAlias("l");
		load.SetHandler((a, b) =>
		{
			s_gameIsLoaded = LoadGame(a, b);
		}, gamePath, pid);
		inRoot.AddCommand(load);
		AddExportCommand(inRoot);
		AddSearchCommand(inRoot);
	}

	private static void AddExportCommand(RootCommand inRoot)
	{
		Command export = new("export");
		export.AddAlias("e");
		Argument<string> name = new("name", "The name of the ebx to export, can include wildcards '?' and '*'");
		Option<bool> convert = new("--convert", "Convert the ebx to a readable format (dbx) and if possible to a common format for e.g. textures, meshes, sounds, etc. ");
        Option<bool> preserveStructure = new("--preserve-structure", "Preserves the folder structure of the ebx");
		Option<string> type = new("--type", "The allowed types of the ebx to export");
		Option<string> output = new("--output", () => string.Empty, "The folder in which the ebx will be exported");
		output.AddAlias("-o");
        Command ebx = new("ebx") { name, convert, preserveStructure, type, output };
		ebx.SetHandler(HandleEbxExport, name, convert, preserveStructure, type, output);
		export.AddCommand(ebx);
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

	private static void HandleEbxExport(string inName, bool inConvert, bool inPreserveStructure, string? inType, string inOutput)
	{
		EbxAssetEntry? ebxAssetEntry = AssetManager.GetEbxAssetEntry(inName);
		if (ebxAssetEntry is null)
		{
			string pattern = "^" + Regex.Escape(inName).Replace("\\?", ".").Replace("\\*", ".*") + "$";
			{
				foreach (EbxAssetEntry item in AssetManager.EnumerateEbxAssetEntries())
				{
					if (Regex.IsMatch(item.Name, pattern) && (string.IsNullOrEmpty(inType) || TypeLibrary.IsSubClassOf(item.Type, inType)))
					{
						ExportEbx(item, inConvert, Path.Combine(inOutput, inPreserveStructure ? item.Name : item.Filename));
					}
				}
				return;
			}
		}
		ExportEbx(ebxAssetEntry, inConvert, Path.Combine(inOutput, inPreserveStructure ? ebxAssetEntry.Name : ebxAssetEntry.Filename));
	}

	private static void ExportEbx(EbxAssetEntry inEntry, bool inConvert, string inPath)
	{
		FileInfo fileInfo = new(inPath);
		fileInfo.Directory?.Create();
		if (inConvert)
		{
			EbxAsset asset = AssetManager.GetEbxAsset(inEntry);
            // TODO: create plugin system
			if (TypeLibrary.IsSubClassOf(inEntry.Type, "TextureAsset"))
			{
				Texture texture = AssetManager.GetResAs<Texture>(AssetManager.GetResAssetEntry(asset.RootObject.GetProperty<ResourceRef>("Resource"))!);
				texture.SaveDds(inPath + ".dds");
			}
			else if (TypeLibrary.IsSubClassOf(inEntry.Type, "MeshAsset"))
			{
				//MeshExporter.Export(ebxAsset, inPath + ".glb");
			}
			using DbxWriter dbxWriter = new(fileInfo.FullName + ".dbx");
			dbxWriter.Write(asset);
			return;
		}
		using Block<byte> block = AssetManager.GetAsset(inEntry);
		File.WriteAllBytes(fileInfo.FullName + ".ebx", block.ToArray());
	}

	private static void HandleQuit()
	{
		Environment.Exit(0);
	}
}