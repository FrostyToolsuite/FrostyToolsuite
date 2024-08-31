namespace Frosty.ModSupport.Mod;

public sealed class FrostyModDetails
{
    public string Title { get; set; } = string.Empty;
    public string Author { get; set;} = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "Misc";
    public string ModPageLink { get; set; } = string.Empty;

    public FrostyModDetails()
    {
    }

    public FrostyModDetails(string inTitle, string inAuthor, string inCategory, string inVersion, string inDescription, string inModPageLink)
    {
        Title = inTitle;
        Author = inAuthor;
        Version = inVersion;
        Description = inDescription;
        Category = inCategory;
        ModPageLink = inModPageLink;
    }

    public override bool Equals(object? obj)
    {
        return obj is FrostyModDetails b && Equals(b);
    }

    public bool Equals(FrostyModDetails b)
    {
        return Title == b.Title && Author == b.Author && Version == b.Version && Description == b.Description  && Category == b.Category && ModPageLink == b.ModPageLink;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Title, Author, Version, Description, Category, ModPageLink);
    }
}