using Shop.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<PriceCalculator>();

var app = builder.Build();

app.MapGet("/price", (decimal unitPrice, int quantity, decimal? discount, PriceCalculator calculator) =>
{
    var total = calculator.LineTotal(unitPrice, quantity);
    return Results.Ok(new { total = discount is { } percent ? calculator.ApplyDiscount(total, percent) : total });
});

app.Run();

public partial class Program;
