using System;
using System.Globalization;
using System.IO;
using Frosty.Sdk;
using Frosty.Sdk.Ebx;
using Frosty.Sdk.IO;
using Frosty.Sdk.Managers;
using Frosty.Sdk.Managers.Entries;
using Frosty.Sdk.Utils;
using Microsoft.Extensions.Logging;
using Sharprompt;

namespace FrostyCli;

internal static partial class Program
{
    #region -- Ebx --

    private static void InteractiveDumpEbx()
    {
        DirectoryInfo dumpDir = RequestDirectory("Input where to dump the ebx", true);

        bool asDbx = Prompt.Confirm("Export the ebx as dbx?");

        if (!Prompt.Confirm("This might take a long time, since it will write all ebx to file, do you want to continue?"))
        {
            return;
        }

        foreach (EbxAssetEntry entry in AssetManager.EnumerateEbxAssetEntries())
        {
            FileInfo file = new(Path.Combine(dumpDir.FullName, $"{entry.Name}.{(asDbx ? "dbx" : "ebx")}"));
            file.Directory?.Create();
            ExportEbx(entry, file, asDbx);
        }
    }

    private static void InteractiveExportEbx()
    {
        string name = Prompt.Select("Select ebx to export", AssetManager.GetEbxNames());

        EbxAssetEntry? entry = AssetManager.GetEbxAssetEntry(name);

        if (entry is null)
        {
            FrostyLogger.Logger?.LogError($"Ebx with name \"{name}\" does not exist.");
            return;
        }

        bool asDbx = Prompt.Confirm("Export the ebx as dbx?");
        string value = asDbx ? "dbx" : "ebx";
        FileInfo? file = RequestFile($"Input where the {value} gets saved to", true,
            $"{entry.Filename}.{value}");

        if (file is null)
        {
            return;
        }

        ExportEbx(entry, file, asDbx);
    }

    private static void ExportEbx(EbxAssetEntry entry, FileInfo inFile, bool inAsDbx)
    {
        if (inAsDbx)
        {
            EbxPartition partition = AssetManager.GetEbxPartiion(entry);

            using (DbxWriter writer = new(inFile.FullName))
            {
                writer.Write(partition);
            }
        }
        else
        {
            using (Block<byte> data = AssetManager.GetAsset(entry))
            {
                using (FileStream stream = inFile.OpenWrite())
                {
                    stream.Write(data);
                }
            }
        }
    }

    #endregion

    #region -- Res --

    private static void InteractiveDumpRes()
    {
        DirectoryInfo dumpDir = RequestDirectory("Input where to dump the res", true);

        bool addMeta = Prompt.Confirm("Export the res with its meta as the first 16 bytes?");

        if (!Prompt.Confirm("This might take a long time, since it will write all res to file, do you want to continue?"))
        {
            return;
        }

        foreach (ResAssetEntry entry in AssetManager.EnumerateResAssetEntries())
        {
            FileInfo file = new(Path.Combine(dumpDir.FullName, $"{entry.Name}.{entry.ResType}"));
            file.Directory?.Create();
            ExportRes(entry, file, addMeta);
        }
    }

    private static void InteractiveExportRes()
    {
        string name = Prompt.Select("Select res to export", AssetManager.GetResNames());

        ResAssetEntry? entry;
        if (ulong.TryParse(name, NumberStyles.HexNumber, null, out ulong rid))
        {
            entry = AssetManager.GetResAssetEntry(rid);
        }
        else
        {
            entry = AssetManager.GetResAssetEntry(name);
        }
        if (entry is null)
        {
            FrostyLogger.Logger?.LogError("Asset does not exist.");
            return;
        }

        bool addMeta = Prompt.Confirm("Export the res with its meta as the first 16 bytes?");

        FileInfo? file = RequestFile("Input where the res gets saved to", true, $"{entry.Filename}.{entry.ResType}");

        if (file is null)
        {
            return;
        }

        ExportRes(entry, file, addMeta);
    }

    private static void ExportRes(ResAssetEntry inEntry, FileInfo inFile, bool inAddMeta)
    {
        using (Block<byte> data = AssetManager.GetAsset(inEntry))
        {
            using (FileStream stream = inFile.OpenWrite())
            {
                if (inAddMeta)
                {
                    stream.Write(inEntry.ResMeta);
                }
                stream.Write(data);
            }
        }
    }

    #endregion

    #region -- Chunks --

    private static void InteractiveDumpChunks()
    {
        DirectoryInfo dumpDir = RequestDirectory("Input where to dump the chunks", true);

        if (!Prompt.Confirm("This might take a long time, since it will write all chunks to file, do you want to continue?"))
        {
            return;
        }

        foreach (ChunkAssetEntry entry in AssetManager.EnumerateChunkAssetEntries())
        {
            FileInfo file = new(Path.Combine(dumpDir.FullName, entry.Name));
            file.Directory?.Create();
            ExportChunk(entry, file);
        }
    }

    private static void InteractiveExportChunk()
    {
        Guid id = Prompt.Select("Select chunk to export", AssetManager.GetChunkIds());

        ChunkAssetEntry? entry = AssetManager.GetChunkAssetEntry(id);

        if (entry is null)
        {
            FrostyLogger.Logger?.LogError("Asset does not exist.");
            return;
        }

        FileInfo? file = RequestFile("Input where the chunk gets saved to", true, entry.Filename);

        if (file is null)
        {
            return;
        }

        ExportChunk(entry, file);
    }

    private static void ExportChunk(ChunkAssetEntry inEntry, FileInfo inFile)
    {
        using (Block<byte> data = AssetManager.GetAsset(inEntry))
        {
            using (FileStream stream = inFile.OpenWrite())
            {
                stream.Write(data);
            }
        }
    }

    #endregion
}