namespace Frosty.ModSupport.Mod;

public sealed class FrostyModDetails
{
    public string Title { get; }
    public string Author { get; }
    public string Version { get; }
    public string Description { get; }
    public string Category => string.IsNullOrEmpty(m_category) ? "Misc" : m_category;
    public string ModPageLink { get; }

    private readonly string m_category;

    public FrostyModDetails(string inTitle, string inAuthor, string inCategory, string inVersion, string inDescription, string inModPageLink)
    {
        Title = inTitle;
        Author = inAuthor;
        Version = inVersion;
        Description = inDescription;
        m_category = inCategory;
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