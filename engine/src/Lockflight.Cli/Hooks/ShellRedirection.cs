namespace Lockflight.Cli.Hooks;

/// <summary>
/// A redirection operator read at a position of a command: how many characters it spans, the token it produces (null for an fd
/// duplication such as <c>2&gt;&amp;1</c>, which writes nothing; <see cref="ShellTokenKind.HereText"/> for a here-string, whose next
/// word is the text read), and for a here-document whether its body strips leading tabs.
/// </summary>
internal sealed record ShellRedirection(int Length, ShellTokenKind? Kind, bool? HereDocumentStripsTabs)
{
    private const string WordEnders = ";|&<>()";

    /// <summary>The redirection starting at <paramref name="index"/>; null when none starts there.</summary>
    public static ShellRedirection? Read(string command, int index)
    {
        char At(int offset) => index + offset < command.Length ? command[index + offset] : '\0';

        return command[index] switch
        {
            '&' when At(1) == '>' => new ShellRedirection(At(2) == '>' ? 3 : 2, ShellTokenKind.WriteRedirect, null),
            '>' => Output(command, index, At(1) is '>' or '|' ? 2 : 1),
            '<' when At(1) == '<' && At(2) == '<' => new ShellRedirection(3, ShellTokenKind.HereText, null),
            '<' when At(1) == '<' => new ShellRedirection(At(2) == '-' ? 3 : 2, ShellTokenKind.InputRedirect, At(2) == '-'),
            '<' when At(1) == '>' => new ShellRedirection(2, ShellTokenKind.WriteRedirect, null),
            '<' when At(1) == '&' => new ShellRedirection(2 + DescriptorLength(command, index + 2), null, null),
            '<' => new ShellRedirection(1, ShellTokenKind.InputRedirect, null),
            _ => null,
        };
    }

    /// <summary><c>&gt;&amp;N</c> and <c>&gt;&amp;-</c> duplicate a descriptor; <c>&gt;&amp;file</c> writes the file like <c>&amp;&gt;</c>.</summary>
    private static ShellRedirection Output(string command, int index, int length)
    {
        if (index + length >= command.Length || command[index + length] != '&')
        {
            return new ShellRedirection(length, ShellTokenKind.WriteRedirect, null);
        }

        int descriptor = DescriptorLength(command, index + length + 1);
        return descriptor > 0
            ? new ShellRedirection(length + 1 + descriptor, null, null)
            : new ShellRedirection(length + 1, ShellTokenKind.WriteRedirect, null);
    }

    /// <summary>The length of an fd number or <c>-</c> at <paramref name="start"/>; 0 when a file name follows instead.</summary>
    private static int DescriptorLength(string command, int start)
    {
        int end = start;
        while (end < command.Length && (char.IsAsciiDigit(command[end]) || command[end] == '-'))
        {
            end++;
        }

        bool endsTheWord = end == command.Length || char.IsWhiteSpace(command[end]) || WordEnders.Contains(command[end], StringComparison.Ordinal);
        return end > start && endsTheWord ? end - start : 0;
    }
}
