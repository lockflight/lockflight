using System.Text.Json;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hooks.Events;

namespace Lockflight.Cli.Tests.Hooks;

public sealed class HookHandlersTests
{
    [Fact]
    public void EveryKnownEventHasExactlyOneHandler()
    {
        foreach (HookEvent hookEvent in Enum.GetValues<HookEvent>().Where(hookEvent => hookEvent != HookEvent.Unknown))
        {
            Assert.Single(HookHandlers.All, handler => handler.Event == hookEvent);
        }

        Assert.Null(HookHandlers.For(HookEvent.Unknown));
    }

    [Fact]
    public void EveryEventThePluginWiresHasAHandlerAndOnlyPreToolUseAsksClaudeCodeToBlockOnFailure()
    {
        string hooksJson = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(ShimLocation.Path)!)!, "hooks", "hooks.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(hooksJson));

        foreach (JsonProperty wired in document.RootElement.GetProperty("hooks").EnumerateObject())
        {
            Assert.True(Enum.TryParse(wired.Name, out HookEvent hookEvent) && HookHandlers.For(hookEvent) is not null, $"no handler for {wired.Name}");
            bool blocksOnFailure = wired.Value.EnumerateArray()
                .SelectMany(entry => entry.GetProperty("hooks").EnumerateArray())
                .Any(hook => hook.TryGetProperty("onFailure", out JsonElement onFailure) && onFailure.GetString() == "block");
            Assert.Equal(hookEvent == HookEvent.PreToolUse, blocksOnFailure);
        }
    }

    [Fact]
    public void AnEngineErrorBlocksOnlyAToolCallOrAnInSessionCommand()
    {
        HookFailureState plain = new(ExpandingSessionCommand: false);

        Assert.Equal([HookEvent.PreToolUse], HookHandlers.All.Where(handler => handler.OnEngineError(plain) == HookFailurePolicy.Block).Select(handler => handler.Event));
        Assert.Equal(HookFailurePolicy.Block, HookHandlers.For(HookEvent.UserPromptExpansion)!.OnEngineError(new HookFailureState(ExpandingSessionCommand: true)));
    }
}
