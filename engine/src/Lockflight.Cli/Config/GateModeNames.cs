namespace Lockflight.Cli.Config;

/// <summary>The manifest spellings of <see cref="GateMode"/>: <c>fail</c>, <c>report</c>, <c>off</c>.</summary>
internal static class GateModeNames
{
    public const string Choices = "fail, report or off";

    public static bool TryParse(string? name, out GateMode mode)
    {
        GateMode? parsed = name switch
        {
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
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown gate mode."),
    };
}
