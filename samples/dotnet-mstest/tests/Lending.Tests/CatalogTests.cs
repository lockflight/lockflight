namespace Lending.Tests;

[TestClass]
public sealed class CatalogTests
{
    [TestMethod]
    [TestProperty("Requirement", "FR-001")]
    public void AddingCopiesCountsThem()
    {
        Catalog catalog = new();
        catalog.AddCopies("Dune", 2);
        catalog.AddCopies("Dune", 1);

        Assert.AreEqual(3, catalog.Copies("Dune"));
    }

    [TestMethod]
    [DataRow("Dune")]
    [DataRow("Emma")]
    [TestProperty("Requirement", "FR-002")]
    public void AnUnknownTitleHasNoCopies(string title) => Assert.AreEqual(0, new Catalog().Copies(title));
}
