using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Frosty.Sdk.Utils;
using Microsoft.Win32.SafeHandles;

namespace Frosty.Sdk.IO;

struct PatternType
{
    public bool IsWildcard;
    public byte Value;
}

public partial class MemoryReader : IDisposable
{
    private const int PROCESS_WM_READ = 0x0010;

    #region -- Windows --

    private enum SystemErrorCode
    {
        InvalidParameter = 0x57,
        PartialCopy = 0x12B
    }

    [Flags]
    private enum AllocationType
    {
        Commit = 0x1000
    }

    [Flags]
    private enum ProtectionType
    {
        NoAccess = 0x1,
        Guard = 0x100
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadProcessMemory(SafeProcessHandle processHandle, nint address, nint bytes, nint size, out nint bytesReadCount);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint VirtualQueryEx(SafeProcessHandle processHandle, nint address, out MemoryBasicInformation64 memoryInformation, nint size);

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private readonly record struct MemoryBasicInformation64([field: FieldOffset(0x0)] long BaseAddress, [field: FieldOffset(0x18)] nint RegionSize, [field: FieldOffset(0x20)] AllocationType State, [field: FieldOffset(0x24)] ProtectionType Protect);

    #endregion

    #region -- Linux --

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct iovec
    {
        public void* iov_base;
        public nuint iov_len;
    }

    [LibraryImport("libc")]
    private static unsafe partial nint process_vm_readv(int pid, iovec* local_iov, nuint liovcnt, iovec* remote_iov,
        nuint riovcnt, nuint flags);

    #endregion

    public virtual long Position
    {
        get => m_position;
        set => m_position = value;
    }

    private Process m_process;
    private ProcessModule m_module;
    protected readonly byte[] m_buffer = new byte[20];
    protected long m_position;

    public MemoryReader(Process inProcess, ProcessModule inModule)
    {
        m_process = inProcess;
        m_position = inModule.BaseAddress;
        m_module = inModule;
    }

    public MemoryReader(Process inProcess, long inModule)
    {
        m_process = inProcess;
        m_position = inModule;
    }

    public virtual void Dispose()
    {
    }

    public void Pad(int alignment)
    {
        if (Position % alignment != 0)
        {
            Position += alignment - Position % alignment;
        }
    }

    public byte ReadByte()
    {
        FillBuffer(1);
        return m_buffer[0];
    }

    public short ReadShort(bool pad = true)
    {
        if (pad)
        {
            Pad(sizeof(short));
        }

        FillBuffer(2);
        return (short)(m_buffer[0] | m_buffer[1] << 8);
    }

    public ushort ReadUShort(bool pad = true)
    {
        if (pad)
        {
            Pad(sizeof(ushort));
        }

        FillBuffer(2);
        return (ushort)(m_buffer[0] | m_buffer[1] << 8);
    }

    public int ReadInt(bool pad = true)
    {
        if (pad)
        {
            Pad(sizeof(int));
        }

        FillBuffer(4);
        return m_buffer[0] | m_buffer[1] << 8 | m_buffer[2] << 16 | m_buffer[3] << 24;
    }

    public uint ReadUInt(bool pad = true)
    {
        if (pad)
        {
            Pad(sizeof(uint));
        }

        FillBuffer(4);
        return (uint)(m_buffer[0] | m_buffer[1] << 8 | m_buffer[2] << 16 | m_buffer[3] << 24);
    }

    public long ReadLong(bool pad = true)
    {
        if (pad)
        {
            Pad(sizeof(long));
        }

        FillBuffer(8);
        return (long)(uint)(m_buffer[4] | m_buffer[5] << 8 | m_buffer[6] << 16 | m_buffer[7] << 24) << 32 |
               (long)(uint)(m_buffer[0] | m_buffer[1] << 8 | m_buffer[2] << 16 | m_buffer[3] << 24);
    }

    public ulong ReadULong(bool pad = true)
    {
        if (pad)
        {
            Pad(sizeof(ulong));
        }

        FillBuffer(8);
        return (ulong)(uint)(m_buffer[4] | m_buffer[5] << 8 | m_buffer[6] << 16 | m_buffer[7] << 24) << 32 |
               (ulong)(uint)(m_buffer[0] | m_buffer[1] << 8 | m_buffer[2] << 16 | m_buffer[3] << 24);
    }

    public Guid ReadGuid(bool pad = true)
    {
        if (pad)
        {
            Pad(4);
        }

        FillBuffer(16);
        return new Guid(new byte[] {
                    m_buffer[0], m_buffer[1], m_buffer[2], m_buffer[3], m_buffer[4], m_buffer[5], m_buffer[6], m_buffer[7],
                    m_buffer[8], m_buffer[9], m_buffer[10], m_buffer[11], m_buffer[12], m_buffer[13], m_buffer[14], m_buffer[15]
                });
    }

    public string ReadNullTerminatedString(bool pad = true)
    {
        if (pad)
        {
            Pad(sizeof(long));
        }

        long offset = ReadLong();
        long orig = Position;
        Position = offset;

        StringBuilder sb = new();
        while (true)
        {
            char c = (char)ReadByte();
            if (c == 0x00)
            {
                break;
            }

            sb.Append(c);
        }

        Position = orig;
        return sb.ToString();
    }

    public byte[]? ReadBytes(int numBytes)
    {
        byte[] outBuffer = new byte[numBytes];

        uint oldProtect = 0;
        int bytesRead = 0;

        m_position += numBytes;
        return outBuffer;
    }

    public unsafe IList<long> Scan(string pattern)
    {
        List<long> retList = new List<long>();
        pattern = pattern.Replace(" ", "");

        PatternType[] bytePattern = new PatternType[pattern.Length / 2];
        for (int i = 0; i < bytePattern.Length; i++)
        {
            string str = pattern.Substring(i * 2, 2);
            bytePattern[i] = new PatternType() { IsWildcard = (str == "??"), Value = (str != "??") ? byte.Parse(pattern.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber) : (byte)0x00 };
        }

        bool bFound = false;

        long pos = Position;
        byte[]? buf = ReadBytes(1024 * 1024);
        byte* startPtr = buf == null ? null : (byte*)Marshal.UnsafeAddrOfPinnedArrayElement(buf, 0);
        byte* ptr = startPtr;
        byte* endPtr = ptr + (1024 * 1024);
        byte* tmpPtr = ptr;

        while (buf != null)
        {
            if (*ptr == bytePattern[0].Value)
            {
                tmpPtr = ptr;
                bFound = true;

                for (int i = 0; i < bytePattern.Length; i++)
                {
                    if (!bytePattern[i].IsWildcard && *tmpPtr != bytePattern[i].Value)
                    {
                        bFound = false;
                        break;
                    }

                    tmpPtr++;
                }

                if (bFound)
                {
                    retList.Add(tmpPtr - startPtr - bytePattern.Length + pos);
                    bFound = false;
                }
            }

            ptr++;
            if (ptr == endPtr)
            {
                pos = Position;
                buf = ReadBytes(1024 * 1024);
                if (buf == null)
                {
                    break;
                }

                startPtr = (byte*)Marshal.UnsafeAddrOfPinnedArrayElement(buf, 0);
                ptr = startPtr;
                endPtr = ptr + (1024 * 1024);
            }
        }

        return retList;
    }

    public IList<nint> ScanPatter(string pattern)
    {
        string[] patternComponents = pattern.Split(' ');
        byte?[] patternBytes = new byte?[patternComponents.Length];

        for (int i = 0; i < patternComponents.Length; i++)
        {
            if (patternComponents[i] == "??")
            {
                patternBytes[i] = null;
            }
            else
            {
                patternBytes[i] = Convert.ToByte(patternComponents[i], 16);
            }
        }

        int[] shiftTable = new int[256];
        int defaultShift = patternBytes.Length;
        int lastWildcardIndex = Array.LastIndexOf(patternBytes, "??");

        if (lastWildcardIndex != -1)
        {
            defaultShift -= lastWildcardIndex;
        }

        Array.Fill(shiftTable, defaultShift);

        for (int i = 0; i < patternBytes.Length - 1; i++)
        {
            byte? @byte = patternBytes[i];

            if (@byte is not null)
            {
                shiftTable[@byte.Value] = patternBytes.Length - 1 - i;
            }
        }

        List<nint> occurrences = new();

        foreach ((nint Address, int Size) region in GetRegions())
        {
            Block<byte> regionBytes = new(region.Size);

            ReadMemory(region.Address, regionBytes, out nint bytesRead);

            for (int i = patternBytes.Length - 1; i < bytesRead; i += shiftTable[regionBytes[i]])
            {
                for (int j = patternBytes.Length - 1; patternBytes[j] is null || patternBytes[j] == regionBytes[i - patternBytes.Length + 1 + j]; j--)
                {
                    if (j == 0)
                    {
                        occurrences.Add(m_module.BaseAddress + i - patternBytes.Length + 1);
                        break;
                    }
                }
            }

            regionBytes.Dispose();
        }

        return occurrences;
    }

    private IEnumerable<(nint Address, int Size)> GetRegions()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            nint currentAddress = 0;

            while (true)
            {
                if (VirtualQueryEx(m_process.SafeHandle, currentAddress, out MemoryBasicInformation64 region, Unsafe.SizeOf<MemoryBasicInformation64>()) == 0)
                {
                    if (Marshal.GetLastPInvokeError() == (int) SystemErrorCode.InvalidParameter)
                    {
                        break;
                    }

                    throw new Win32Exception();
                }

                if (region.State.HasFlag(AllocationType.Commit) && region.Protect != ProtectionType.NoAccess && !region.Protect.HasFlag(ProtectionType.Guard))
                {
                    yield return (currentAddress, (int) region.RegionSize);
                }

                currentAddress = (nint) region.BaseAddress + region.RegionSize;
            }
        }
        else
        {
            string path = $"/proc/{m_process.Id}/maps";
            foreach (string region in File.ReadLines(path))
            {
                string[] arr = region.Split(' ');
                int index = arr[0].IndexOf('-');
                nint start = nint.Parse(arr[0][..index]);
                nint end = nint.Parse(arr[0][(index + 1)..]);

                // TODO: check for access

                yield return (start, (int)(end - start));
            }
        }
    }

    private unsafe void ReadMemory(nint inAddress, Block<byte> outData, out nint bytesRead)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (!ReadProcessMemory(m_process.SafeHandle, inAddress, (nint)outData.Ptr, outData.Size, out bytesRead))
            {
                throw new Win32Exception();
            }
        }
        else
        {
            iovec localIo = new() { iov_base = outData.Ptr, iov_len = (nuint)outData.Size };
            iovec remoteIo = new() { iov_base = inAddress.ToPointer(), iov_len = (nuint)outData.Size };

            if ((bytesRead = process_vm_readv(m_process.Id, &localIo, 1, &remoteIo, 1, 0)) == -1)
            {
                throw new Exception();
            }
        }
    }

    protected virtual void FillBuffer(int numBytes)
    {
        uint oldProtect = 0;
        int bytesRead = 0;

        m_position += numBytes;
    }
}