namespace Lockflight.Cli.Ci;

/// <summary>
/// What the value-flow pass (<see cref="IlValueFlow"/>) knows of one value on a method's evaluation stack.
/// </summary>
/// <param name="FromLiterals">Whether the value is built from string literals and constants only, through <c>Path.Combine</c>,
/// <c>Path.Join</c> or <c>string.Concat</c> of the runtime: no argument, field, call result, or local that ever held another value.</param>
/// <param name="Literal">The string literal itself when the value is exactly one <c>ldstr</c>; null otherwise.</param>
internal sealed record FlowValue(bool FromLiterals, string? Literal)
{
    public static readonly FlowValue Unknown = new(false, null);

    public static readonly FlowValue Constant = new(true, null);
}
