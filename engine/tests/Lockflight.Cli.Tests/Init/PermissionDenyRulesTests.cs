using System.Text.Json.Nodes;
using Lockflight.Cli.Config;
using Lockflight.Cli.Init;

namespace Lockflight.Cli.Tests.Init;

public sealed class PermissionDenyRulesTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory("lockflight-deny-");

    [Fact]
    public void APlainProtectedPathGetsAnEditRuleOnlyWhenItExists()
    {
        Write(Manifest.FileName, "version: 1\n");

        IReadOnlyList<string> rules = PermissionDenyRules.For(root.FullName, DefaultProtectedPaths.Globs);

        Assert.Contains("Edit(/lockflight.yaml)", rules);
        Assert.Contains("Edit(/.claude/settings.json)", rules);
        Assert.DoesNotContain("Edit(/global.json)", rules);
        Assert.DoesNotContain("Edit(/.claude/hooks/**)", rules);
    }

    [Fact]
    public void APatternIsWrittenAsItIsAndAnExistingDirectoryAsATree()
    {
        Write(".claude/hooks/guard.sh", "exit 0");

        IReadOnlyList<string> rules = PermissionDenyRules.For(root.FullName, DefaultProtectedPaths.Globs);

        Assert.Contains("Edit(/**/Directory.Build.props)", rules);
        Assert.Contains("Edit(/.claude/hooks/**)", rules);
    }

    [Fact]
    public void TheLockflightDirectoryIsCoveredByItsProtectedPartsOnly()
    {
        Write(".lockflight/baselines/known-failing-tests.json", "[]");
        Write(".lockflight/engine.lock", "{}");
        Write(".lockflight/ci/install.sh", "exit 0");
        Write(".lockflight/local/engine.lock", "{}");

        IReadOnlyList<string> rules = PermissionDenyRules.For(root.FullName, DefaultProtectedPaths.Globs);

        Assert.Contains("Edit(/.lockflight/bin/**)", rules);
        Assert.Contains("Edit(/.lockflight/baselines/**)", rules);
        Assert.Contains("Edit(/.lockflight/engine.lock)", rules);
        Assert.Contains("Edit(/.lockflight/ci/**)", rules);
        Assert.Contains("Edit(/.lockflight/local/engine.lock)", rules);
        Assert.DoesNotContain("Edit(/.lockflight/local/**)", rules);
        Assert.Contains("Edit(/.lockflight/features/*/approvals/**)", rules);
        Assert.DoesNotContain("Edit(/.lockflight/**)", rules);
    }

    [Fact]
    public void TheUsersLockflightInstallIsDenied() =>
        Assert.Contains("Edit(~/.lockflight/**)", PermissionDenyRules.For(root.FullName, DefaultProtectedPaths.Globs));

    [Fact]
    public void MergeAddsMissingRulesAfterTheTeamsOwn()
    {
        JsonObject settings = JsonNode.Parse("""{ "permissions": { "allow": ["Bash(dotnet build)"], "deny": ["Read(./.env)", "Edit(/lockflight.yaml)"] } }""")!.AsObject();

        bool changed = PermissionDenyRules.Merge(settings, ["Edit(/lockflight.yaml)", "Edit(/global.json)"]);

        Assert.True(changed);
        Assert.Equal(["Read(./.env)", "Edit(/lockflight.yaml)", "Edit(/global.json)"], settings["permissions"]!["deny"]!.AsArray().Select(rule => rule!.GetValue<string>()));
        Assert.Equal("Bash(dotnet build)", settings["permissions"]!["allow"]![0]!.GetValue<string>());
        Assert.False(PermissionDenyRules.Merge(settings, ["Edit(/global.json)"]));
    }

    [Theory]
    [InlineData("""{ "permissions": [] }""")]
    [InlineData("""{ "permissions": { "deny": "Edit(/x)" } }""")]
    [InlineData("""{ "permissions": { "deny": [1] } }""")]
    public void PermissionsOfAnotherShapeAreAProblem(string json) =>
        Assert.NotNull(PermissionDenyRules.Problem(JsonNode.Parse(json)!.AsObject()));

    private void Write(string relative, string text)
    {
        string path = Path.Combine(root.FullName, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public void Dispose() => root.Delete(recursive: true);
}
