namespace Lockflight.Cli.Gates.Parsers;

/// <summary>
/// One compiler or analyzer warning/error. <see cref="Fingerprint"/> deliberately leaves out the line number, which moves with
/// every unrelated edit; code + file + message identifies the diagnostic across builds.
/// </summary>
internal sealed record BuildDiagnostic(string Code, string RelativeFile, int Line, string Message)
{
    public string Fingerprint => $"{Code}|{RelativeFile}|{Message}";

    public string Describe() => $"{Code} {RelativeFile}:{Line}: {Message}";
}
