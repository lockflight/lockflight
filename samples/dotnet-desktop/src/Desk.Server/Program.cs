using Desk.Server;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<PriceList>();
WebApplication app = builder.Build();

app.MapGet("/prices/{item}", (string item, PriceList prices) =>
    prices.PriceOf(item) is decimal price ? Results.Ok(price) : Results.NotFound());

app.Run();

// Public so the tests can start the API in memory with WebApplicationFactory<Program>.
public partial class Program
{
}
