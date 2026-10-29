using System.Text;
using System.Text.Json;
using Lockflight.Cli.Hooks;
using Lockflight.Cli.Hooks.Adapters;
using Lockflight.Cli.Hooks.Requests;

namespace Lockflight.Cli.Tests.Hooks;

/// <summary>A <see cref="HookRequest"/> written as the <c>lockflight.hook-request/v1</c> document the neutral adapter reads.</summary>
internal static class NeutralRequestJson
{
    public static string Write(HookRequest request, HookEvent hookEvent)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter json = new(stream))
        {
            json.WriteStartObject();
            json.WriteString("schema", NeutralAdapter.Schema);
            json.WriteString("event", NeutralAdapter.EventName(hookEvent));
            json.WriteStartObject("tool");
            json.WriteString("name", request.Tool.Name);
            json.WriteString("kind", KindName(request.Tool.Kind));
            json.WriteBoolean("writesFiles", request.Tool.WritesFiles);
            json.WriteString("shell", request.Tool.Shell switch { ShellDialect.Posix => "posix", ShellDialect.PowerShell => "powershell", _ => null });
            json.WriteEndObject();
            json.WriteStartArray("paths");
            foreach (string path in request.Paths)
            {
                json.WriteStringValue(path);
            }

            json.WriteEndArray();
            json.WriteString("command", request.Command);
            json.WriteStartObject("session");
            json.WriteString("id", request.Session.Id);
            json.WriteString("agent", request.Session.Agent ?? "main");
            json.WriteString("projectDir", request.Session.ProjectDirectory);
            json.WriteEndObject();
            json.WriteString("cwd", request.WorkingDirectory);
            if (request.Prompt is HookPrompt prompt)
            {
                json.WriteStartObject("prompt");
                json.WriteString("name", prompt.Name);
                json.WriteString("source", prompt.Source);
                json.WriteString("args", prompt.Arguments);
                json.WriteEndObject();
            }
            else
            {
                json.WriteNull("prompt");
            }

            if (request.TaskSubject is string subject)
            {
                json.WriteStartObject("task");
                json.WriteString("subject", subject);
                json.WriteEndObject();
            }
            else
            {
                json.WriteNull("task");
            }

            json.WriteStartArray("inputStrings");
            foreach (string value in request.InputStrings)
            {
                json.WriteStringValue(value);
            }

            json.WriteEndArray();
            json.WriteStartObject("client");
            json.WriteString("name", "test");
            json.WriteNull("version");
            json.WriteEndObject();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string KindName(ToolKind kind) => kind switch
    {
        ToolKind.Read => "read",
        ToolKind.Search => "search",
        ToolKind.Edit => "edit",
        ToolKind.Write => "write",
        ToolKind.Shell => "shell",
        ToolKind.Notebook => "notebook",
        ToolKind.Fetch => "fetch",
        ToolKind.Agent => "agent",
        _ => "other",
    };
}
