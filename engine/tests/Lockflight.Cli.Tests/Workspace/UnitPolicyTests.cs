using Lockflight.Cli.Paths;
using Lockflight.Cli.Workspace;

namespace Lockflight.Cli.Tests.Workspace;

public sealed class UnitPolicyTests
{
    [Theory]
    [InlineData("services/orders/src/Contracts/**", "src/Contracts/**")]
    [InlineData("services/*/Migrations/**", "Migrations/**")]
    [InlineData("*/*/appsettings.Production.json", "appsettings.Production.json")]
    [InlineData("services/**/db/**", "**/db/**")]
    [InlineData("**/appsettings.Local.json", "**/appsettings.Local.json")]
    [InlineData("services/orders", "**")]
    [InlineData("services/**", "**")]
    [InlineData("services/**.cs", "**.cs")]
    [InlineData("services/ord**/db/**", "**/db/**")]
    [InlineData("services/**orders/*.cs", "**orders/*.cs", "*.cs")]
    [InlineData("services/**/orders/*.cs", "**/orders/*.cs", "*.cs")]
    [InlineData("services/billing/**")]
    [InlineData("web/*/dist/**")]
    [InlineData("secrets/**")]
    public void ShellGlobsAreRebasedWhenTheyCanMatchInsideTheUnit(string glob, params string[] expected)
    {
        Assert.Equal(expected, UnitPolicy.Rebase(glob, "services/orders"));
    }

    [Theory]
    [InlineData("services/**")]
    [InlineData("services/**.cs")]
    [InlineData("services/ord**/db/**")]
    [InlineData("services/**orders/*.cs")]
    [InlineData("services/**/orders/*.cs")]
    [InlineData("services/**/db/**")]
    [InlineData("**/appsettings.Local.json")]
    [InlineData("services/*/Migrations/**")]
    [InlineData("s*/o?ders/**/*.json")]
    public void ARebasedGlobProtectsEveryPathInTheUnitTheShellsGlobProtects(string glob)
    {
        string[] paths = ["a.cs", "src/a.cs", "orders/a.cs", "src/orders/a.cs", "db/x.sql", "src/db/x.sql", "Migrations/1.cs", "appsettings.Local.json", "src/appsettings.Local.json", "config/a.json", "a/b/c.json"];
        IReadOnlyList<string> rebased = UnitPolicy.Rebase(glob, "services/orders");

        string[] lost = [.. paths.Where(path => PathRules.Matches(glob, $"services/orders/{path}") && !rebased.Any(unitGlob => PathRules.Matches(unitGlob, path)))];

        Assert.Empty(lost);
    }
}
