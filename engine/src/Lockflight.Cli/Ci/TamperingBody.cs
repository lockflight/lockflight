using System.Reflection.Metadata;

namespace Lockflight.Cli.Ci;

/// <summary>
/// What a method body of a scanned test assembly does (<see cref="HostTamperingScanner"/>): the outermost type it belongs to, the
/// members it calls (an empty type for one of the assembly itself), the strings it loads, and the outermost types of the assembly
/// whose fields it reads or whose methods it calls.
/// </summary>
internal sealed record TamperingBody(TypeDefinitionHandle Owner, List<(string Type, string Member)> Calls, List<string> Strings, HashSet<TypeDefinitionHandle> Touched);
