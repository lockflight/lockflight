using System.Text.Json.Nodes;
using Lockflight.Cli.Gates.Parsers;

namespace Lockflight.Cli.Tests.Gates.Parsers;

public sealed class SecretLeaksTests
{
    private const string RedGreen = ".lockflight/features/001-shop/redgreen.json";
    private const string Sha256 = "a466cc5558316d8973d54a75b9fa7946db9ff2ac4caf1c61641ff8c9b915ca40";
    private const string UpperSha256 = "A466CC5558316D8973D54A75B9FA7946DB9FF2AC4CAF1C61641FF8C9B915CA40";
    private const string Commit = "3f9a2c1e7b4d8a6f0e5c2b9d1a7e4f3c8b6d0a2e";

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
    [InlineData(".lockflight/features/001-shop/redgreen.json", "TokenTests.cs\": \"" + Sha256 + "\"", Sha256)]
    [InlineData(".lockflight\\features\\001-shop\\redgreen.json", "TokenTests.cs\": \"" + Sha256 + "\"", Sha256)]
    [InlineData(".lockflight/features/001-shop/approvals/tasks.json", "contracts/auth-api.md\": \"" + Sha256 + "\"", Sha256)]
    [InlineData("services/orders/.lockflight/features/001-shop/reviews/run-1.json", "tokenHead\":  \"" + Commit + "\"", Commit)]
    public void AHashTheEngineRecordedInItsEvidenceIsNotAFinding(string file, string match, string secret) =>
        Assert.Empty(SecretLeaks.Parse(Report("generic-api-key", file, match, secret)));

    [Theory]
    [InlineData("slack-bot-token", RedGreen, "TokenTests.cs\": \"" + Sha256 + "\"", Sha256)]
    [InlineData("generic-api-key", "notes/hashes.json", "TokenTests.cs\": \"" + Sha256 + "\"", Sha256)]
    [InlineData("generic-api-key", ".lockflight/features/001-shop/tasks.json", "TokenTests.cs\": \"" + Sha256 + "\"", Sha256)]
    [InlineData("generic-api-key", ".lockflight/baselines/redgreen.json", "TokenTests.cs\": \"" + Sha256 + "\"", Sha256)]
    [InlineData("generic-api-key", RedGreen, "api_token\": \"Zq8vR2mK7tLp4XwN9cJd3HfB6sYe1GuA\"", "Zq8vR2mK7tLp4XwN9cJd3HfB6sYe1GuA")]
    [InlineData("generic-api-key", RedGreen, "TokenTests.cs\": \"" + Sha256 + "0\"", Sha256 + "0")]
    [InlineData("generic-api-key", RedGreen, "TokenTests.cs\": \"" + UpperSha256 + "\"", UpperSha256)]
    [InlineData("generic-api-key", RedGreen, "token=" + Sha256 + "\": \"" + Sha256 + "\"", Sha256)]
    [InlineData("generic-api-key", RedGreen, "TokenTests.cs\": \"" + Sha256 + "\"", "REDACTED")]
    public void AnythingElseInOrBesideTheEvidenceIsStillAFinding(string rule, string file, string match, string secret) =>
        Assert.Single(SecretLeaks.Parse(Report(rule, file, match, secret)));

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void EmptyReportsHaveNoFindings(string report) => Assert.Empty(SecretLeaks.Parse(report));

    private static string Report(string rule, string file, string match, string secret) =>
        new JsonArray(new JsonObject { ["RuleID"] = rule, ["File"] = file, ["StartLine"] = 3, ["Match"] = match, ["Secret"] = secret }).ToJsonString();
}
