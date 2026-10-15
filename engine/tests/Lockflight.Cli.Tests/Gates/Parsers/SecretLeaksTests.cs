using Lockflight.Cli.Gates.Parsers;

namespace Lockflight.Cli.Tests.Gates.Parsers;

public sealed class SecretLeaksTests
{
    [Fact]
    public void FindingsAreReadWithoutTheSecretAndDeduplicated()
    {
        const string report = """
            [
              { "RuleID": "aws-access-token", "File": "src\\Keys.cs", "StartLine": 7, "Commit": "", "Secret": "REDACTED" },
              { "RuleID": "aws-access-token", "File": "src/Keys.cs", "StartLine": 7, "Commit": "", "Secret": "REDACTED" },
              { "RuleID": "generic-api-key", "File": "appsettings.json", "StartLine": 3, "Commit": "0123456789abcdef" }
            ]
            """;

        IReadOnlyList<SecretLeak> leaks = SecretLeaks.Parse(report);

        Assert.Equal(2, leaks.Count);
        Assert.Equal("aws-access-token at src/Keys.cs:7", leaks[0].Describe());
        Assert.Equal("generic-api-key at appsettings.json:3 (01234567)", leaks[1].Describe());
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void EmptyReportsHaveNoFindings(string report) => Assert.Empty(SecretLeaks.Parse(report));
}
