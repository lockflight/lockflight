namespace Lockflight.Cli.Config;

/// <summary>The manifest spellings of <see cref="GateMode"/>: <c>fail</c>, <c>report</c>, <c>off</c> and, where a step allows it, <c>auto</c>.</summary>
internal static class GateModeNames
{
    public const string Choices = "fail, report or off";

    public const string ChoicesWithAuto = "auto, fail, report or off";

    public static bool TryParse(string? name, out GateMode mode) => TryParse(name, allowsAuto: false, out mode);

    public static bool TryParse(string? name, bool allowsAuto, out GateMode mode)
    {
        GateMode? parsed = name switch
        {
            "auto" when allowsAuto => GateMode.Auto,
            "fail" => GateMode.Fail,
            "report" => GateMode.Report,
            "off" => GateMode.Off,
            _ => null,
        };
        mode = parsed ?? default;
        return parsed is not null;
    }

    public static string ToName(GateMode mode) => mode switch
    {
        GateMode.Fail => "fail",
        GateMode.Report => "report",
        GateMode.Off => "off",
        GateMode.Auto => "auto",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown gate mode."),
    };
}
