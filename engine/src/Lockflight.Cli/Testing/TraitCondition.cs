using System.Text.RegularExpressions;

namespace Lockflight.Cli.Testing;

/// <summary>
/// One VSTest-style trait condition, <c>Name=Value</c> (only tests with that trait) or <c>Name!=Value</c> (every test but
/// those): the form of <c>test.stopFilter</c> that every runner can apply and that the engine can evaluate on a listing. Trait
/// names and values compare without case, as the runners compare them.
/// </summary>
internal sealed partial record TraitCondition(string Name, string Value, bool Negated)
{
    /// <summary>The condition a filter holds; null when it is anything else (a combination, another property, an operator such as <c>~</c>).</summary>
    public static TraitCondition? Parse(string filter) =>
        Condition().Match(filter) is { Success: true } match
            ? new TraitCondition(match.Groups["name"].Value, match.Groups["value"].Value, match.Groups["not"].Success)
            : null;

    /// <summary>Whether a run with this condition leaves the test out.</summary>
    public bool Excludes(TestCase test)
    {
        bool has = test.Traits.Any(trait =>
            string.Equals(trait.Key, Name, StringComparison.OrdinalIgnoreCase) && string.Equals(trait.Value, Value, StringComparison.OrdinalIgnoreCase));
        return Negated ? has : !has;
    }

    /// <summary><c>Name=Value</c>, the trait as xUnit's and Microsoft.Testing.Platform's trait options take it.</summary>
    public string Trait => $"{Name}={Value}";

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*(?<not>!)?=\s*(?<value>[^&|()!=]+?)\s*$")]
    private static partial Regex Condition();
}
