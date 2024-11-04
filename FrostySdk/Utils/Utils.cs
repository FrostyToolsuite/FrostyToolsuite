using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Frosty.Sdk.Ebx;
using Frosty.Sdk.IO;

namespace Frosty.Sdk.Utils;

public static class Utils
{
    public static string BaseDirectory { get; set; } = string.Empty;

    public static int HashString(string value, bool toLower = false)
    {
        const uint kOffset = 5381;
        const uint kPrime = 33;

        uint hash = kOffset;
        for (int i = 0; i < value.Length; i++)
        {
            hash = (hash * kPrime) ^ (byte)(toLower ? char.ToLower(value[i]) : value[i]);
        }

        return (int)hash;
    }

    public static int HashStringA(string value, bool toLower = false)
    {
        const uint kOffset = 5381;
        const uint kPrime = 33;

        uint hash = kOffset;
        for (int i = 0; i < value.Length; i++)
        {
            hash ^= (byte)(toLower ? char.ToLower(value[i]) : value[i]);
            hash *= kPrime;
        }

        return (int)hash;
    }

    public static Guid GenerateDeterministicGuid(IEnumerable<object> objects, string type, Guid fileGuid)
    {
        return GenerateDeterministicGuid(objects, TypeLibrary.GetType(type)!, fileGuid);
    }

    public static Guid GenerateDeterministicGuid(IEnumerable<object> objects, Type type, Guid fileGuid)
    {
        Guid outGuid;

        int createCount = 0;
        HashSet<Guid> existingGuids = new();
        foreach (dynamic obj in objects)
        {
            AssetClassGuid objGuid = obj.GetInstanceGuid();
            existingGuids.Add(objGuid.ExportedGuid);
            createCount++;
        }

        Block<byte> buffer = new(stackalloc byte[20]);

        Span<byte> result = stackalloc byte[16];
        while (true)
        {
            // generate a deterministic unique guid
            using (DataStream writer = new(buffer.ToStream()))
            {
                writer.WriteGuid(fileGuid);
                writer.WriteInt32(++createCount);
            }

            MD5.HashData(buffer.ToSpan(), result);
            outGuid = new Guid(result);

            if (!existingGuids.Contains(outGuid))
            {
                break;
            }
        }

        buffer.Dispose();

        return outGuid;
    }

    public static Sha1 GenerateSha1(ReadOnlySpan<byte> buffer)
    {
        Span<byte> hashed = stackalloc byte[20];
        SHA1.HashData(buffer, hashed);
        Sha1 newSha1 = new(hashed);
        return newSha1;
    }

    public static ulong GenerateResourceId()
    {
        Random random = new();

        const ulong min = ulong.MinValue;
        const ulong max = ulong.MaxValue;

        const ulong uRange = max - min;
        ulong ulongRand;

        Span<byte> buf = stackalloc byte[8];
        do
        {
            random.NextBytes(buf);
            ulongRand = BinaryPrimitives.ReadUInt64LittleEndian(buf);

        } while (ulongRand > max - (max % uRange + 1) % uRange);

        return (ulongRand % uRange + min) | 1;
    }

    public static class File
    {
        public static FileSystemInfo CreateSymbolicLink(string inPath, string inPathToTarget)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // windows is ass and needs admin rights for symlinks
                ProcessStartInfo startInfo = new()
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c mklink \"{inPath}\" \"{inPathToTarget}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas"
                };

                using (Process? process = Process.Start(startInfo))
                {
                    process?.WaitForExit();

                    if (process?.ExitCode != 0)
                    {
                        string? error = process?.StandardError.ReadToEnd();
                        throw new Exception($"Faile to create symbolic link: {error}");
                    }

                    return new FileInfo(inPath);
                }
            }
            return System.IO.File.CreateSymbolicLink(inPath, inPathToTarget);
        }
    }

    public static class Directory
    {
        public static FileSystemInfo CreateSymbolicLink(string inPath, string inPathToTarget)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // windows is ass and needs admin rights for symlinks
                ProcessStartInfo startInfo = new()
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c mklink /D \"{inPath}\" \"{inPathToTarget}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas"
                };

                using (Process? process = Process.Start(startInfo))
                {
                    process?.WaitForExit();

                    if (process?.ExitCode != 0)
                    {
                        string? error = process?.StandardError.ReadToEnd();
                        throw new Exception($"Faile to create symbolic link: {error}");
                    }

                    return new DirectoryInfo(inPath);
                }
            }
            return System.IO.Directory.CreateSymbolicLink(inPath, inPathToTarget);
        }
    }
}