namespace Lockflight.Cli.Adoption;

/// <summary>
/// What a Spec Kit upgrade did: files replaced by the new stock, files added, and customised files kept as they were. A
/// customised file the Lockflight preset then replaced (the preset owns the commands it overrides, such as tasks) is listed in
/// <see cref="ReplacedByPreset"/>, not as kept. Files that are a symbolic link, or lie in a linked folder, are never written
/// (the backup does not follow links, so undo could not restore what they point to) and are listed in <see cref="Linked"/>.
/// </summary>
internal sealed record SpecKitMergeResult(IReadOnlyList<string> Upgraded, IReadOnlyList<string> Added, IReadOnlyList<string> Customised)
{
    public IReadOnlyList<string> ReplacedByPreset { get; init; } = [];

    public IReadOnlyList<string> Linked { get; init; } = [];
}
