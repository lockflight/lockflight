namespace Lockflight.Cli.Hosting;

/// <summary>
/// One engine command as documents may name it: its words (<c>gate run</c>), the options it accepts and whether arguments that
/// are not options follow the words (<c>propose &lt;path&gt;</c>).
/// </summary>
internal sealed record CatalogedCommand(IReadOnlyList<string> Words, IReadOnlyList<string> Options, bool TakesArguments)
{
    public string Name => string.Join(' ', Words);
}
