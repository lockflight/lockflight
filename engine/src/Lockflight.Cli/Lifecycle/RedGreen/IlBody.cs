using System.Reflection.Metadata;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>What a method body's IL does that the test code scanner reads: the methods it names, and whether it throws or rethrows.</summary>
internal sealed record IlBody(IReadOnlyList<EntityHandle> MethodTokens, bool Throws);
