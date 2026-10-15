using Lockflight.Cli.Adoption;

namespace Lockflight.Cli.Tests.Adoption;

public sealed class ManifestListEditorTests
{
    [Fact]
    public void EntriesGoAfterTheLastItemAndCommentsSurvive()
    {
        const string text = "version: 1\n# extra paths\nprotected:\n  - \"secrets/**\"   # keys\nstop:\n  gate: false\n";

        string? edited = ManifestListEditor.Append(text, "protected", [["\"**/Contracts/**\""]]);

        Assert.Equal("version: 1\n# extra paths\nprotected:\n  - \"secrets/**\"   # keys\n  - \"**/Contracts/**\"\nstop:\n  gate: false\n", edited);
    }

    [Fact]
    public void AnEmptyFlowListBecomesABlockList()
    {
        string? edited = ManifestListEditor.Append("version: 1\nprotected: []\nstop:\n  gate: false\n", "protected", [["\"a/**\""], ["\"b/**\""]]);

        Assert.Equal("version: 1\nprotected:\n  - \"a/**\"\n  - \"b/**\"\nstop:\n  gate: false\n", edited);
    }

    [Fact]
    public void AMissingListIsAddedAtTheEndWithMappingEntries()
    {
        string? edited = ManifestListEditor.Append("version: 1\n", "units", [["path: services/orders"], ["path: web/admin", "kind: none"]]);

        Assert.Equal("version: 1\nunits:\n  - path: services/orders\n  - path: web/admin\n    kind: none\n", edited);
    }

    [Fact]
    public void ANonEmptyFlowListCannotBeExtendedInPlace()
    {
        Assert.Null(ManifestListEditor.Append("protected: [a, b]\n", "protected", [["c"]]));
    }
}
