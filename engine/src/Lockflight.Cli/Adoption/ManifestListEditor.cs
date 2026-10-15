using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace Lockflight.Cli.Adoption;

/// <summary>
/// Adds entries to a top-level list of <c>lockflight.yaml</c> (<c>protected:</c>, <c>units:</c>) without rewriting the rest, so
/// comments and layout survive: new lines go after the list's last entry, or a new list goes at the end of the file. A list in
/// flow style (<c>[a, b]</c>) cannot be extended in place; then null is returned and the caller shows the lines to add.
/// </summary>
internal static class ManifestListEditor
{
    /// <param name="entries">Each entry as the lines of one list item without the leading <c>- </c> (more lines for a mapping).</param>
    public static string? Append(string text, string key, IReadOnlyList<IReadOnlyList<string>> entries)
    {
        if (entries.Count == 0)
        {
            return text;
        }

        YamlStream stream = new();
        try
        {
            stream.Load(new StringReader(text));
        }
        catch (YamlException)
        {
            return null;
        }

        YamlMappingNode? root = stream.Documents.Count == 0 ? null : stream.Documents[0].RootNode as YamlMappingNode;
        YamlNode? list = root?.Children.FirstOrDefault(pair => pair.Key is YamlScalarNode { Value: string name } && name == key).Value;
        if (list is null)
        {
            string body = string.Concat(entries.Select(entry => Item(entry, "  ")));
            return text + (text.Length == 0 || text.EndsWith('\n') ? string.Empty : "\n") + $"{key}:\n" + body;
        }

        string[] lines = text.Split('\n');
        if (list is YamlSequenceNode { Style: SequenceStyle.Flow, Children.Count: 0 } empty)
        {
            // "key: []" (what init writes) becomes a block list.
            int keyLine = (int)root!.Children.First(pair => pair.Key is YamlScalarNode { Value: string name } && name == key).Key.Start.Line;
            if (empty.Start.Line != keyLine || lines[keyLine - 1].Trim() != $"{key}: []")
            {
                return null;
            }

            lines[keyLine - 1] = $"{key}:";
            string items = string.Concat(entries.Select(entry => Item(entry, "  "))).TrimEnd('\n');
            return string.Join('\n', lines.Take(keyLine)) + "\n" + items + "\n" + string.Join('\n', lines.Skip(keyLine));
        }

        if (list is not YamlSequenceNode { Style: not SequenceStyle.Flow } sequence || sequence.Children.Count == 0)
        {
            return null;
        }

        int indent = (int)sequence.Children[0].Start.Column - 3;
        int insertAfter = (int)sequence.Children[^1].End.Line;
        if (sequence.Children[^1].End.Column == 1)
        {
            // A mapping item ends at the start of the next line.
            insertAfter--;
        }

        string added = string.Concat(entries.Select(entry => Item(entry, new string(' ', Math.Max(indent, 0)))));
        return string.Join('\n', lines.Take(insertAfter)) + "\n" + added + string.Join('\n', lines.Skip(insertAfter));
    }

    private static string Item(IReadOnlyList<string> lines, string indent) =>
        $"{indent}- {lines[0]}\n" + string.Concat(lines.Skip(1).Select(line => $"{indent}  {line}\n"));
}
