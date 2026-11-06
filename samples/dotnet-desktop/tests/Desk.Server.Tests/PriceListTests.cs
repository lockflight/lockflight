using Microsoft.AspNetCore.Mvc.Testing;

namespace Desk.Server.Tests;

public class PriceListTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void AKnownItemHasItsPrice() => Assert.Equal(1.50m, new PriceList().PriceOf("pen"));

    [Fact]
    public async Task TheApiAnswersAKnownItem()
    {
        using HttpClient client = factory.CreateClient();

        string body = await client.GetStringAsync("/prices/notebook");

        Assert.Equal("4.00", body);
    }
}
