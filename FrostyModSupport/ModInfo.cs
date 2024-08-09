namespace Frosty.ModSupport;

public class ModInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;

    public override bool Equals(object? obj)
    {
        if (obj is ModInfo other)
        {
            return Equals(other);
        }

        return false;
    }

    public bool Equals(ModInfo other)
    {
        return Name == other.Name && Version == other.Version && Category == other.Category && FileName == other.FileName;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, Version, Category, Link, FileName);
    }
}