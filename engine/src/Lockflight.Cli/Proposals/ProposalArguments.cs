namespace Lockflight.Cli.Proposals;

/// <summary>
/// Parses <c>propose &lt;path&gt; (--from &lt;file&gt; | --delete) [&lt;path&gt; (--from &lt;file&gt; | --delete) …]</c>. Every path takes
/// exactly one of the two; anything else is a usage error, so a typo never proposes the wrong change.
/// </summary>
internal static class ProposalArguments
{
    public const string From = "--from";

    public const string Delete = "--delete";

    public static ProposalArgumentsParse Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return ProposalArgumentsParse.Failed($"name a project file and its new content: propose <path> {From} <file>, or <path> {Delete}");
        }

        List<ProposalRequest> requests = [];
        int index = 0;
        while (index < args.Count)
        {
            string path = args[index++];
            if (path.StartsWith('-'))
            {
                return ProposalArgumentsParse.Failed(path is From or Delete ? $"{path} must follow the project path it is for" : Unknown(path));
            }

            if (index >= args.Count || !args[index].StartsWith('-'))
            {
                return ProposalArgumentsParse.Failed(NeedsSource(path));
            }

            string option = args[index++];
            if (option == Delete)
            {
                requests.Add(new ProposalRequest(path, null));
            }
            else if (option != From)
            {
                return ProposalArgumentsParse.Failed(Unknown(option));
            }
            else if (index >= args.Count || args[index].Length == 0 || args[index].StartsWith('-'))
            {
                return ProposalArgumentsParse.Failed($"{From} needs a value: the file that holds the complete new content of '{path}'");
            }
            else
            {
                requests.Add(new ProposalRequest(path, args[index++]));
            }

            if (index < args.Count && args[index] is From or Delete)
            {
                return ProposalArgumentsParse.Failed($"{NeedsSource(path)}, not both");
            }
        }

        return ProposalArgumentsParse.Parsed(requests);
    }

    private static string NeedsSource(string path) => $"'{path}' needs {From} <file> or {Delete}";

    private static string Unknown(string option) => $"unknown option '{option}'; use {From} <file> or {Delete} after each path";
}
