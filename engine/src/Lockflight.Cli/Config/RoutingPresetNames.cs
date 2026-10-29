namespace Lockflight.Cli.Config;

/// <summary>The manifest spellings of <see cref="RoutingPreset"/>: <c>relaxed</c>, <c>default</c>, <c>strict</c>.</summary>
internal static class RoutingPresetNames
{
    public const string Choices = "relaxed, default or strict";

    public static bool TryParse(string? name, out RoutingPreset preset)
    {
        RoutingPreset? parsed = name switch
        {
            "relaxed" => RoutingPreset.Relaxed,
            "default" => RoutingPreset.Default,
            "strict" => RoutingPreset.Strict,
            _ => null,
        };
        preset = parsed ?? default;
        return parsed is not null;
    }

    public static string ToName(RoutingPreset preset) => preset switch
    {
        RoutingPreset.Relaxed => "relaxed",
        RoutingPreset.Default => "default",
        RoutingPreset.Strict => "strict",
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown routing preset."),
    };
}
