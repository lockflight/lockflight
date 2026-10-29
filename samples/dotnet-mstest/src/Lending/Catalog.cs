namespace Lending;

public sealed class Catalog
{
    private readonly Dictionary<string, int> copies = new(StringComparer.Ordinal);

    public void AddCopies(string title, int count) => copies[title] = Copies(title) + count;

    public int Copies(string title) => copies.GetValueOrDefault(title);
}
