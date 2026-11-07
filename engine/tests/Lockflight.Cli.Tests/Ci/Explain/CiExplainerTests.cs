using System.Text.Json.Nodes;
using Lockflight.Cli.Ci;
using Lockflight.Cli.Ci.Explain;
using Lockflight.Cli.Reasons;
using Lockflight.Cli.Tests.Goldens;
using Lockflight.Cli.Tests.Schemas;

namespace Lockflight.Cli.Tests.Ci.Explain;

public sealed class CiExplainerTests
{
    private static readonly string FixtureDirectory = Path.Combine(SchemaFiles.RepositoryRoot, "engine", "tests", "Lockflight.Cli.Tests", "Ci", "Explain", "Fixtures");

    public static TheoryData<string> Fixtures() =>
        [.. Directory.EnumerateFiles(FixtureDirectory, "*.json").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal).OfType<string>()];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EachFixtureIsExplainedAsItsGoldenSays(string fixture)
    {
        CiExplanation explanation = Explain(Fixture(fixture), $"{fixture}.json");

        GoldenFiles.Verify($"ci-explain/{fixture}", CiExplanationText.Render(explanation));
    }

    [Fact]
    public void EveryReasonCodeRendersItsNameNextLineAndEntry()
    {
        Assert.All(ReasonCodeNames.Named, code =>
        {
            string name = ReasonCodeNames.ToName(code)!;
            JsonObject evidence = Fixture("green-before");
            evidence["checks"]![0]!["reason"] = name;

            string text = CiExplanationText.Render(Explain(evidence, "evidence.json"));

            Assert.Contains($"Reason: {name}\n", text, StringComparison.Ordinal);
            Assert.Contains($"Next: {ReasonCatalogue.Next(code)}\n", text, StringComparison.Ordinal);
            Assert.Contains($"See: {ReasonCatalogue.Link(code)}\n", text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ACheckWithoutAReasonPointsAtTheGuide()
    {
        JsonObject evidence = Fixture("green-before");
        evidence["checks"]![0]!.AsObject().Remove("reason");

        string text = CiExplanationText.Render(Explain(evidence, "evidence.json"));

        Assert.Contains("Reason: none named\n", text, StringComparison.Ordinal);
        Assert.Contains($"See: {ReasonCatalogue.GuideUrl}\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGatesCheckCarriesTheStepsThatDidNotPass()
    {
        ExplainedCheck gates = Assert.Single(Explain(Fixture("scanner-replaced"), "evidence.json").Checks);

        ExplainedCheck step = Assert.Single(gates.Steps);
        Assert.Equal("security.secrets", step.Id);
        Assert.Equal(ReasonCode.ScannerReplaced, step.Reason.Code);
    }

    [Fact]
    public void AnOffCheckAndAnAdvisoryCheckThatDidNotRunAreLeftOutAndAnAdvisoryFailIsShown()
    {
        JsonObject evidence = Fixture("green-before");
        JsonObject existing = evidence["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == "tests.existingChanged")!.AsObject();
        existing["verdict"] = "FAIL";
        existing["reason"] = "EXISTING_TEST_PASSES_ON_BASE";

        CiExplanation explanation = Explain(evidence, "evidence.json");

        Assert.DoesNotContain(explanation.Checks, check => check.Id is "approvals" or "review");
        Assert.Contains(explanation.Checks, check => check.Id == "tests.existingChanged" && check.Requirement == "advisory");
    }

    [Fact]
    public void ARequiredCheckThatDidNotApplyIsShownSinceItWithholdsThePass()
    {
        JsonObject evidence = Fixture("local-pass");
        JsonObject trace = evidence["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == "trace")!.AsObject();
        trace["verdict"] = "N/A";

        Assert.Contains(Explain(evidence, "last.json").Checks, check => check.Id == "trace");
    }

    [Theory]
    [InlineData("green-before", "evidence")]
    [InlineData("local-pass", "local-evidence")]
    [InlineData("review-findings", "verdict")]
    [InlineData("engine-lock-refused", "refusal")]
    [InlineData("judge-did-not-finish", "judge-missing")]
    [InlineData("gate-result", "gate-result")]
    public void TheKindOfFileIsToldFromWhatItsWriterSets(string fixture, string kind) =>
        Assert.Equal(kind, ExplainedDocumentNames.ToName(Explain(Fixture(fixture), "file.json").Document));

    [Fact]
    public void AFileOfAnotherSchemaIsNotExplained()
    {
        CiExplanationReading reading = CiExplainer.Explain(new JsonObject { ["schema"] = "lockflight.blocked/v1" }, "blocked.json", null);

        Assert.Null(reading.Explanation);
        Assert.Contains("lockflight.blocked/v1", reading.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void TheJsonFormNamesEachCheckWithItsReasonNextLineAndLink()
    {
        JsonObject json = CiExplanationJson.Render([Explain(Fixture("compile-red"), "evidence.json")]);

        JsonNode explanation = json["explanations"]![0]!;
        Assert.Equal("evidence", explanation["kind"]!.GetValue<string>());
        Assert.Equal("FAIL", explanation["contexts"]!["lockflight/judge"]!.GetValue<string>());
        JsonNode red = explanation["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == "red")!;
        Assert.Equal("COMPILE_RED", red["reason"]!.GetValue<string>());
        Assert.True(red["known"]!.GetValue<bool>());
        Assert.Equal(ReasonCatalogue.Next(ReasonCode.CompileRed), red["next"]!.GetValue<string>());
        Assert.Equal(ReasonCatalogue.Link(ReasonCode.CompileRed), red["link"]!.GetValue<string>());
    }

    [Fact]
    public void TheJsonFormKeepsAnUnknownReasonAsWrittenWithoutANextLine()
    {
        JsonObject json = CiExplanationJson.Render([Explain(Fixture("unknown-reason"), "evidence.json")]);

        JsonNode routing = json["explanations"]![0]!["checks"]!.AsArray().Single(check => check!["id"]!.GetValue<string>() == "routing")!;
        Assert.Equal("SOMETHING_NEWER", routing["reason"]!.GetValue<string>());
        Assert.False(routing["known"]!.GetValue<bool>());
        Assert.Null(routing["next"]);
    }

    [Fact]
    public void EscapeSequencesAndLineBreaksInTheFileAreShownVisiblyNotActedOn()
    {
        const string Escape = "\u001b]52;c;cGF5bG9hZA==\u0007\u001b[2K\rPASS";
        JsonObject evidence = Fixture("green-before");
        evidence["head"] = $"aaaa{Escape}";
        evidence["verdict"] = $"FAIL{Escape}";
        evidence["contexts"]![$"gates{Escape}"] = $"PASS\nFAIL{Escape}";
        JsonObject check = evidence["checks"]![0]!.AsObject();
        check["id"] = $"red{Escape}";
        check["requirement"] = $"required{Escape}";
        check["verdict"] = $"FAIL{Escape}";
        check["why"] = $"line one\nPASS red (fake)\r\n{Escape}\u202Eevil";
        check["reason"] = $"GREEN_BEFORE{Escape}";
        evidence["blocked"] = new JsonObject
        {
            ["reason"] = new JsonObject { ["code"] = $"X{Escape}", ["summary"] = $"wait{Escape}" },
            ["needs"] = new JsonArray(new JsonObject { ["who"] = $"owner{Escape}", ["detail"] = $"do{Escape}" }),
        };

        CiExplanation explanation = CiExplainer.Explain(evidence, $"evidence{Escape}.json", $"origin{Escape}").Explanation!;
        string text = CiExplanationText.Render(explanation);
        string json = CiExplanationJson.Render([explanation]).ToJsonString();

        Assert.DoesNotContain(text, character => char.IsControl(character) && character != '\n');
        Assert.DoesNotContain('\u202E', text);
        Assert.Contains("^[]52;c;cGF5bG9hZA==^G^[[2K PASS", text, StringComparison.Ordinal);
        Assert.Contains("Detail: line one PASS red (fake) ", text, StringComparison.Ordinal);
        Assert.Contains("<U+202E>evil", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u001b", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\n", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ADetailWithoutControlCharactersIsShownAsWritten()
    {
        JsonObject evidence = Fixture("green-before");
        evidence["checks"]![0]!["why"] = "tests: Shop.Tests.CartTests.Adds passed before src/Cart.cs changed";

        string text = CiExplanationText.Render(Explain(evidence, "evidence.json"));

        Assert.Contains("Detail: tests: Shop.Tests.CartTests.Adds passed before src/Cart.cs changed\n", text, StringComparison.Ordinal);
    }

    private static CiExplanation Explain(JsonObject document, string source) =>
        CiExplainer.Explain(document, source, null).Explanation ?? throw new InvalidOperationException($"{source} was not explained");

    private static JsonObject Fixture(string name) => CiArtifactReader.Read(Path.Combine(FixtureDirectory, $"{name}.json")).Document!;
}
