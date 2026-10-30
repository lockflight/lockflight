using Lockflight.Cli.Routing;

namespace Lockflight.Cli.Tests.Routing;

public sealed class CSharpDeclarationsTests
{
    private const string Controller = """
        using Microsoft.AspNetCore.Mvc;

        namespace Shop.Api;

        // public class Commented { }
        [ApiController]
        [Route("api/carts")]
        public sealed class CartController(ICarts carts) : ControllerBase
        {
            private const string Note = "public class InString { }";

            [HttpGet("{id}")]
            public IActionResult Get(int id)
            {
                if (id < 0) { return BadRequest(); }
                return Ok(carts.Find(id));
            }

            [HttpPost, Authorize]
            public IActionResult Add([FromBody] Cart cart) => Ok();

            internal int Count { get; set; } = 3;

            private void Helper<T>() where T : class { }
        }

        public interface ICarts
        {
            Cart Find(int id);
        }

        public enum Status { Open = 1, Closed = 2 }

        public delegate void Changed(int id);

        public sealed record Cart(int Id, string Name);
        """;

    [Fact]
    public void DeclarationsKeepTheirPlaceAttributesAndStringsButNotTheirBodies()
    {
        IReadOnlyList<CSharpDeclaration> declarations = CSharpDeclarations.Extract(Controller);

        Assert.Equal(
            [
                "Type Shop.Api CartController public [ApiController]|[Route(\"api/carts\")] : ControllerBase",
                "Member Shop.Api.CartController Note private ",
                "Member Shop.Api.CartController Get public [HttpGet(\"{id}\")]",
                "Member Shop.Api.CartController Add public [HttpPost,Authorize]",
                "Member Shop.Api.CartController Count internal ",
                "Member Shop.Api.CartController Helper private ",
                "Type Shop.Api ICarts public ",
                "Member Shop.Api.ICarts Find public ",
                "Type Shop.Api Status public ",
                "Type Shop.Api Changed public ",
                "Type Shop.Api Cart public ",
            ],
            declarations.Select(declaration =>
                $"{declaration.Kind} {declaration.Container} {declaration.Name} {(declaration.PublicSurface ? "public" : declaration.Signature.StartsWith("private", StringComparison.Ordinal) ? "private" : "internal")} {string.Join('|', declaration.Attributes)}{(declaration.BaseList.Length > 0 ? " : " + declaration.BaseList : string.Empty)}"));
        Assert.Equal("public enum Status { Open = 1,Closed = 2 }", declarations.Single(declaration => declaration.Name == "Status").Signature);
        Assert.Equal(12, declarations.Single(declaration => declaration.Name == "Get").Line);
    }

    [Fact]
    public void AReorderedAttributeListOrAReflowIsTheSameDeclaration()
    {
        string reflowed = Controller
            .Replace("[ApiController]\n[Route(\"api/carts\")]", "[Route(\"api/carts\")]\n[ApiController]", StringComparison.Ordinal)
            .Replace("public IActionResult Get(int id)", "public IActionResult Get(\n        int id)", StringComparison.Ordinal);

        Assert.Equal(CSharpDeclarations.Extract(Controller).Select(declaration => declaration.Key), CSharpDeclarations.Extract(reflowed).Select(declaration => declaration.Key));
    }

    [Fact]
    public void ARouteTemplateEditChangesTheDeclaration()
    {
        string edited = Controller.Replace("[HttpGet(\"{id}\")]", "[HttpGet(\"{id:int}\")]", StringComparison.Ordinal);

        Assert.NotEqual(
            CSharpDeclarations.Extract(Controller).Single(declaration => declaration.Name == "Get").Key,
            CSharpDeclarations.Extract(edited).Single(declaration => declaration.Name == "Get").Key);
    }
}
