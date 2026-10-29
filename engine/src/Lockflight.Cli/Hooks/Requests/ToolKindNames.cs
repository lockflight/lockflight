namespace Lockflight.Cli.Hooks.Requests;

/// <summary>The neutral request's spellings of <see cref="ToolKind"/>.</summary>
internal static class ToolKindNames
{
    public static bool TryParse(string? name, out ToolKind kind)
    {
        ToolKind? parsed = name switch
        {
            "read" => ToolKind.Read,
            "search" => ToolKind.Search,
            "edit" => ToolKind.Edit,
            "write" => ToolKind.Write,
            "shell" => ToolKind.Shell,
            "notebook" => ToolKind.Notebook,
            "fetch" => ToolKind.Fetch,
            "agent" => ToolKind.Agent,
            "other" => ToolKind.Other,
            _ => null,
        };
        kind = parsed ?? default;
        return parsed is not null;
    }
}
