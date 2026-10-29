using Lockflight.Cli.Docs;

namespace Lockflight.Cli.Tests.Docs;

public sealed class InstructionFilesTests
{
    [Theory]
    [InlineData("CLAUDE.md", true)]
    [InlineData("services/orders/CLAUDE.md", true)]
    [InlineData("AGENTS.md", true)]
    [InlineData("web/AGENTS.md", true)]
    [InlineData(".claude/rules/testing.md", true)]
    [InlineData(".claude/skills/release/SKILL.md", true)]
    [InlineData(".claude/skills/release/reference/steps.md", true)]
    [InlineData(".claude/agents/reviewer.md", true)]
    [InlineData(".claude/commands/ship.md", true)]
    [InlineData("services/orders/.claude/rules/db.md", true)]
    [InlineData(".claude/settings.json", false)]
    [InlineData(".claude/notes.md", false)]
    [InlineData("README.md", false)]
    [InlineData("docs/guide.md", false)]
    [InlineData("plugins/acme/skills/x/SKILL.md", false)]
    public void TheAgentsInstructionFilesAreTheOnesRead(string path, bool expected) =>
        Assert.Equal(expected, InstructionFiles.IsInstruction(path));
}
