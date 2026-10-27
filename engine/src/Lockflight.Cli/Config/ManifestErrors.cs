using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Config;

/// <summary>Builds the errors of one manifest file, so every section reader reports the same path and 1-based position.</summary>
internal sealed class ManifestErrors(string path)
{
    /// <summary>An error about the file as a whole, reported at line 1, column 1.</summary>
    public ManifestError AtStart(string message) => new(path, 1, 1, message);

    public ManifestError At(Mark mark, string message) => new(path, (int)mark.Line, (int)mark.Column, message);

    public ManifestError NotAName(YamlNode keyNode) => At(keyNode.Start, "every key must be a plain name, not a list or a mapping");
}
