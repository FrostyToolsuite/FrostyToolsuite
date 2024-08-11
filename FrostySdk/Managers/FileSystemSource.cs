using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Frosty.Sdk.Managers;

public class FileSystemSource
{
    public static readonly FileSystemSource Base = new("Data", Type.Base);

    public static FileSystemSource Patch => ProfilesLibrary.FrostbiteVersion > "2014.4.11"
        ? new FileSystemSource("Patch", Type.Patch)
        : new FileSystemSource("Update/Patch/Data", Type.Patch);

    public enum Type
    {
        LCU,
        DLC,
        Patch,
        Base
    }

    public string Path { get; }
    private readonly Type m_type;

    public FileSystemSource(string inPath, Type inType)
    {
        Path = inPath;
        m_type = inType;
    }

    public bool TryResolvePath(string inPath, [NotNullWhen(true)] out string? resolvedPath)
    {
        string path = System.IO.Path.Combine(FileSystemManager.BasePath, Path, inPath);

        if (File.Exists(path) || Directory.Exists(path))
        {
            resolvedPath = path;
            return true;
        }

        resolvedPath = null;
        return false;
    }

    /// <summary>
    /// Resolves path in current Source.
    /// Returns the path, even if it doesn't exist.
    /// </summary>
    /// <param name="inPath"></param>
    /// <returns></returns>
    public string ResolvePath(string inPath)
    {
        return System.IO.Path.Combine(FileSystemManager.BasePath, Path, inPath);
    }

    public bool IsDLC() => m_type == Type.DLC;
}