using Lockflight.Cli.Adoption;

namespace Lockflight.Cli.Tests.Adoption;

public sealed class SpecKitMergeTests : IDisposable
{
    private readonly DirectoryInfo work = Directory.CreateTempSubdirectory("lockflight-skmerge-");

    private string Root => Path.Combine(work.FullName, "project");

    private string Old => Path.Combine(work.FullName, "old");

    private string New => Path.Combine(work.FullName, "new");

    [Fact]
    public void StockFilesAreUpgradedCustomisedOnesKeptAndNewOnesAdded()
    {
        Write(Old, ".specify/templates/spec-template.md", "spec v1");
        Write(New, ".specify/templates/spec-template.md", "spec v2");
        Write(Root, ".specify/templates/spec-template.md", "spec v1");
        Write(Old, ".specify/memory/constitution.md", "stock constitution");
        Write(New, ".specify/memory/constitution.md", "stock constitution v2");
        Write(Root, ".specify/memory/constitution.md", "# Acme constitution");
        Write(New, ".claude/skills/speckit-converge/SKILL.md", "new skill");
        Write(Old, ".specify/init-options.json", "{\"speckit_version\":\"1.1.0\",\"at\":1}");
        Write(New, ".specify/init-options.json", "{\"speckit_version\":\"1.1.3\",\"at\":2}");
        Write(Root, ".specify/init-options.json", "{\"speckit_version\":\"1.1.0\",\"at\":0}");
        Write(Root, ".specify/extensions/team/extension.yml", "team");
        Write(New, "README.md", "not Spec Kit's own file");

        SpecKitMergeResult result = SpecKitMerge.Merge(Root, Old, New);

        Assert.Equal([".specify/init-options.json", ".specify/templates/spec-template.md"], result.Upgraded);
        Assert.Equal([".claude/skills/speckit-converge/SKILL.md"], result.Added);
        Assert.Equal([".specify/memory/constitution.md"], result.Customised);
        Assert.Equal("# Acme constitution", Read(".specify/memory/constitution.md"));
        Assert.Equal("spec v2", Read(".specify/templates/spec-template.md"));
        Assert.Equal("team", Read(".specify/extensions/team/extension.yml"));
        Assert.False(File.Exists(Path.Combine(Root, "README.md")));
    }

    [Fact]
    public void AWorkflowRegistryWithTheTeamsOwnWorkflowIsKept()
    {
        Write(Old, ".specify/workflows/workflow-registry.json", "{\"workflows\":{\"speckit\":{\"installed_at\":\"1\"}}}");
        Write(New, ".specify/workflows/workflow-registry.json", "{\"workflows\":{\"speckit\":{\"installed_at\":\"2\"}}}");
        Write(Root, ".specify/workflows/workflow-registry.json", "{\"workflows\":{\"speckit\":{\"installed_at\":\"0\"},\"release\":{}}}");

        SpecKitMergeResult result = SpecKitMerge.Merge(Root, Old, New);

        Assert.Equal([".specify/workflows/workflow-registry.json"], result.Customised);
    }

    [Fact]
    public void AStockWorkflowRegistryIsUpgradedDespiteItsTimestamps()
    {
        Write(Old, ".specify/workflows/workflow-registry.json", "{\"workflows\":{\"speckit\":{\"installed_at\":\"1\"}}}");
        Write(New, ".specify/workflows/workflow-registry.json", "{\"workflows\":{\"speckit\":{\"installed_at\":\"2\"}}}");
        Write(Root, ".specify/workflows/workflow-registry.json", "{\"workflows\":{\"speckit\":{\"installed_at\":\"0\"}}}");

        SpecKitMergeResult result = SpecKitMerge.Merge(Root, Old, New);

        Assert.Equal([".specify/workflows/workflow-registry.json"], result.Upgraded);
    }

    private static void Write(string directory, string relative, string text)
    {
        string path = Path.Combine(directory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative));

    public void Dispose() => TempDirectories.DeleteRecursively(work);
}
