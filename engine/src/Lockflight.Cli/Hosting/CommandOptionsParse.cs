namespace Lockflight.Cli.Hosting;

/// <summary>The outcome of parsing a command's options: exactly one of <see cref="Options"/> or <see cref="Error"/> is set.</summary>
internal sealed record CommandOptionsParse(CommandOptions? Options, string? Error)
{
    public static CommandOptionsParse Parsed(CommandOptions options) => new(options, null);

    public static CommandOptionsParse Failed(string error) => new(null, error);
}
