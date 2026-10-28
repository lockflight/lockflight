namespace Lockflight.Cli.Rules;

/// <summary>An imperative sentence or list item taken from an instruction file, with the code spans it names.</summary>
internal sealed record RuleSentence(string File, int Line, string Text, IReadOnlyList<string> Spans);
