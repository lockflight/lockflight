using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace Lockflight.Cli.Ci;

/// <summary>
/// A static scan of a built test assembly (or shared test-helper assembly) for the plain ways to forge a test result, read without
/// loading it: module initializers, process-exit and unhandled-exception handlers, <c>Environment.Exit</c>/<c>FailFast</c>,
/// starting processes, file writes in a method that does not visibly use a temporary folder, reflection in a method that names a
/// test framework's or runner's types, and setting CI variables. A method visibly uses a temporary folder when it asks for one before
/// it writes, or reads a field or calls a method of a type of the assembly that asks for one (a fixture that makes a temporary
/// folder, a test class that writes into its fixture's folder); either way it must name no other folder of the run. A guardrail: an obfuscated call evades it. What it catches is
/// listed, so the code is visible in the pull request's evidence.
/// </summary>
internal static partial class HostTamperingScanner
{
    private const byte TwoBytePrefix = 0xFE;
    private const byte SwitchOpCode = 0x45;
    private const byte StringOpCode = 0x72;

    private static readonly HashSet<byte> MethodOps = [0x27, 0x28, 0x6F, 0x73];
    private static readonly HashSet<byte> TwoByteMethodOps = [0x06, 0x07];

    /// <summary><c>ldfld</c>, <c>ldflda</c>, <c>ldsfld</c>, <c>ldsflda</c>.</summary>
    private static readonly HashSet<byte> FieldLoadOps = [0x7B, 0x7C, 0x7E, 0x7F];

    /// <summary><c>stfld</c>, <c>stsfld</c>.</summary>
    private static readonly HashSet<byte> FieldStoreOps = [0x7D, 0x80];

    private static readonly (string Type, string[] Members, string Kind)[] Calls =
    [
        ("System.AppDomain", ["add_ProcessExit", "add_UnhandledException", "add_DomainUnload"], "process-exit"),
        ("System.Runtime.Loader.AssemblyLoadContext", ["add_Unloading"], "process-exit"),
        ("System.Environment", ["Exit", "FailFast"], "exit"),
        ("System.Diagnostics.Process", ["Start", "Kill"], "process-start"),
        ("System.Diagnostics.ProcessStartInfo", [".ctor"], "process-start"),
    ];

    private static readonly string[] WriteTypes = ["System.IO.File", "System.IO.Directory", "System.IO.FileStream", "System.IO.StreamWriter", "System.IO.FileInfo"];
    private static readonly string[] WriteMembers = ["Write", "Append", "Create", "Delete", "Move", "Copy", "Replace", "Open", ".ctor"];
    private static readonly string[] TempMembers = ["GetTempPath", "GetTempFileName", "CreateTempSubdirectory"];
    private static readonly string[] ReflectionMembers = ["GetType", "GetField", "GetFields", "GetMethod", "GetMethods", "GetProperty", "GetProperties", "GetMember", "InvokeMember", "CreateInstance"];
    private static readonly string[] RunnerNamespaces = ["Xunit", "NUnit", "Microsoft.Testing", "Microsoft.VisualStudio.TestPlatform", "Microsoft.TestPlatform"];
    private static readonly string[] CiVariablePrefixes = ["GITHUB_", "RUNNER_", "LOCKFLIGHT_", "BITBUCKET_", "CI_", "SYSTEM_", "BUILD_"];

    /// <summary>Every finding in the assembly; empty when it cannot be read (the build output is the build's problem, not tampering).</summary>
    public static IReadOnlyList<TamperingFinding> Scan(string assemblyPath)
    {
        try
        {
            using PEReader pe = new(File.OpenRead(assemblyPath));
            MetadataReader reader = pe.GetMetadataReader();
            string assembly = reader.GetString(reader.GetAssemblyDefinition().Name);
            List<TamperingFinding> findings = [];
            List<(string Location, TamperingBody Body)> bodies = [];
            Dictionary<(TypeDefinitionHandle, string), List<string>> attributeStrings = AttributeStrings(reader);
            foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
            {
                TypeDefinition type = reader.GetTypeDefinition(typeHandle);
                foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
                {
                    MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                    string location = Canonical($"{TypeName(reader, typeHandle)}::{reader.GetString(method.Name)}");
                    if (IsModuleInitializer(reader, type, method))
                    {
                        findings.Add(new TamperingFinding(assembly, "module-initializer", location, "runs before any test"));
                    }

                    if (method.RelativeVirtualAddress != 0)
                    {
                        string name = reader.GetString(method.Name);
                        TamperingBody body = Read(reader, pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader(), Outermost(reader, typeHandle), MemberKey(typeHandle, name), name is ".ctor" or ".cctor");
                        body = body with { StubKey = StubKey(reader, typeHandle) };
                        body.AttributeStrings.AddRange(attributeStrings.GetValueOrDefault((Outermost(reader, typeHandle), SourceMethod(reader, typeHandle, method))) ?? []);
                        bodies.Add((location, body));
                    }
                }
            }

            // The types of the assembly that ask for a temporary folder, by their outermost type (an async method's state machine
            // and a lambda's closure are nested types).
            TamperingBody[] all = [.. bodies.Select(body => body.Body)];
            HashSet<string> tempReach = Reach(all, calls => calls.Any(IsTemp), _ => false);
            HashSet<string> elsewhere = Reach(all, NamesAnotherFolder, NamesAResultsPath);
            foreach ((string location, TamperingBody body) in bodies)
            {
                findings.AddRange(Findings(body, tempReach, elsewhere, assembly, location));
            }

            return [.. findings.DistinctBy(finding => (finding.Id, finding.Member))];
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>
    /// A location without the ordinals of compiler-generated names (<c>&lt;Run&gt;d__5</c> → <c>&lt;Run&gt;</c>), which shift when code
    /// is added, so a finding the base already had is recognised at head.
    /// </summary>
    private static string Canonical(string location) => GeneratedOrdinal().Replace(location, "<$1>");

    private static bool IsModuleInitializer(MetadataReader reader, TypeDefinition type, MethodDefinition method)
    {
        string name = reader.GetString(method.Name);
        if (reader.GetString(type.Name) == "<Module>" && name == ".cctor")
        {
            return true;
        }

        return method.GetCustomAttributes().Any(handle => AttributeName(reader, reader.GetCustomAttribute(handle).Constructor) == "System.Runtime.CompilerServices.ModuleInitializerAttribute");
    }

    /// <summary>Walks one method body's IL: the calls, strings, fields and stores in it. Internal so a test can hand it IL bytes.</summary>
    internal static TamperingBody Read(MetadataReader reader, BlobReader il, TypeDefinitionHandle owner, string key, bool constructor)
    {
        TamperingBody body = new(owner, key, [], [], [], [], new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal))
        {
            IsConstructor = constructor,
        };
        (int Calls, int Strings, HashSet<string> Members) segment = (0, 0, new HashSet<string>(StringComparer.Ordinal));
        while (il.RemainingBytes > 0)
        {
            byte code = il.ReadByte();
            if (code == TwoBytePrefix)
            {
                byte second = il.ReadByte();
                if (TwoByteMethodOps.Contains(second))
                {
                    Call(reader, body, segment.Members, MetadataTokens.EntityHandle(il.ReadInt32()));
                }
                else
                {
                    il.Offset += second switch { >= 0x09 and <= 0x0E => 2, 0x12 or 0x19 => 1, 0x15 or 0x16 or 0x1C => 4, _ => 0 };
                }
            }
            else if (MethodOps.Contains(code))
            {
                Call(reader, body, segment.Members, MetadataTokens.EntityHandle(il.ReadInt32()));
            }
            else if (FieldLoadOps.Contains(code))
            {
                EntityHandle field = MetadataTokens.EntityHandle(il.ReadInt32());
                if (field.Kind == HandleKind.FieldDefinition)
                {
                    body.Touched.Add(Outermost(reader, reader.GetFieldDefinition((FieldDefinitionHandle)field).GetDeclaringType()));
                }
                else
                {
                    TouchGeneric(reader, body, field);
                }

                if (OwnKey(reader, field) is string fieldKey)
                {
                    body.Members.Add(fieldKey);
                    segment.Members.Add(fieldKey);
                }
            }
            else if (FieldStoreOps.Contains(code))
            {
                if (OwnKey(reader, MetadataTokens.EntityHandle(il.ReadInt32())) is string fieldKey)
                {
                    body.Stores.Add(fieldKey);
                    body.Segments.Add(new TamperingSegment(fieldKey, body.Calls[segment.Calls..], body.Strings[segment.Strings..], segment.Members));
                }

                segment = (body.Calls.Count, body.Strings.Count, new HashSet<string>(StringComparer.Ordinal));
            }
            else if (code == StringOpCode)
            {
                body.Strings.Add(reader.GetUserString(MetadataTokens.UserStringHandle(il.ReadInt32() & 0x00FFFFFF)));
            }
            else if (code == SwitchOpCode)
            {
                // The count is read first: `il.Offset += il.ReadInt32() * 4` would take the offset before the read moved it,
                // land four bytes short of the jump table's end, and read its last entry as code.
                int targets = il.ReadInt32();
                il.Offset += targets * sizeof(int);
            }
            else
            {
                il.Offset += OperandSize(code);
            }
        }

        return body;
    }

    private static void Call(MetadataReader reader, TamperingBody body, HashSet<string> segment, EntityHandle token)
    {
        body.Calls.Add(Member(reader, token));
        EntityHandle method = token.Kind == HandleKind.MethodSpecification ? reader.GetMethodSpecification((MethodSpecificationHandle)token).Method : token;
        if (method.Kind == HandleKind.MethodDefinition)
        {
            body.Touched.Add(Outermost(reader, reader.GetMethodDefinition((MethodDefinitionHandle)method).GetDeclaringType()));
        }
        else
        {
            TouchGeneric(reader, body, method);
        }

        if (OwnKey(reader, method) is string key)
        {
            body.Members.Add(key);
            segment.Add(key);
        }
    }

    private static string MemberKey(TypeDefinitionHandle type, string name) => $"{MetadataTokens.GetRowNumber(type)}::{name}";

    /// <summary>
    /// The key of a field or method of this assembly a token names (overloads share one, which only marks more); a member of a
    /// generic type of the assembly is keyed by the type it instantiates. Null for another assembly's member.
    /// </summary>
    private static string? OwnKey(MetadataReader reader, EntityHandle token)
    {
        switch (token.Kind)
        {
            case HandleKind.MethodDefinition:
                MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)token);
                return MemberKey(method.GetDeclaringType(), reader.GetString(method.Name));
            case HandleKind.FieldDefinition:
                FieldDefinition field = reader.GetFieldDefinition((FieldDefinitionHandle)token);
                return MemberKey(field.GetDeclaringType(), reader.GetString(field.Name));
            case HandleKind.MemberReference when reader.GetMemberReference((MemberReferenceHandle)token) is { Parent.Kind: HandleKind.TypeSpecification } member
                && GenericDefinition(reader, (TypeSpecificationHandle)member.Parent) is TypeDefinitionHandle definition:
                return MemberKey(definition, reader.GetString(member.Name));
            default:
                return null;
        }
    }

    private static TypeDefinitionHandle? GenericDefinition(MetadataReader reader, TypeSpecificationHandle specification)
    {
        BlobReader signature = reader.GetBlobReader(reader.GetTypeSpecification(specification).Signature);
        if (signature.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
        {
            return null;
        }

        signature.ReadByte();
        return signature.ReadTypeHandle() is { Kind: HandleKind.TypeDefinition } definition ? (TypeDefinitionHandle)definition : null;
    }

    /// <summary>
    /// The members of the assembly whose code, directly or through the assembly's own members, has a property: it calls
    /// (<paramref name="byCalls"/>) or loads a string (<paramref name="byStrings"/>) that has it, calls a member that has it, or
    /// reads a field a member that has it stores to, repeated until nothing changes, so a path that passes through helpers and
    /// fields of the assembly is followed. Members, not types: one test of a class that reads an environment variable leaves the
    /// others alone. A constructor's fields are judged per initializer (<see cref="TamperingSegment"/>), and a constructor marks
    /// no caller; an async or iterator state machine's method marks the method it was generated from.
    /// </summary>
    private static HashSet<string> Reach(IReadOnlyList<TamperingBody> bodies, Func<List<(string Type, string Member)>, bool> byCalls, Func<List<string>, bool> byStrings)
    {
        HashSet<string> marked = new(StringComparer.Ordinal);
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (TamperingBody body in bodies)
            {
                if (body.IsConstructor)
                {
                    foreach (TamperingSegment segment in body.Segments.Where(segment => byCalls(segment.Calls) || byStrings(segment.Strings) || segment.Members.Overlaps(marked)))
                    {
                        changed |= marked.Add(segment.Field);
                    }

                    continue;
                }

                if (byCalls(body.Calls) || byStrings(body.Strings) || byStrings(body.AttributeStrings) || body.Members.Overlaps(marked))
                {
                    changed |= marked.Add(body.Key) | AddAll(marked, body.Stores) | (body.StubKey is string stub && marked.Add(stub));
                }
            }
        }

        return marked;
    }

    /// <summary>For a method of a compiler-generated state machine <c>&lt;Run&gt;d__3</c>, the key of <c>Run</c> on the type that declares it.</summary>
    private static string? StubKey(MetadataReader reader, TypeDefinitionHandle type)
    {
        TypeDefinition definition = reader.GetTypeDefinition(type);
        string name = reader.GetString(definition.Name);
        return !definition.GetDeclaringType().IsNil && name.Contains(">d__", StringComparison.Ordinal) && GeneratedFrom(name) is string method
            ? MemberKey(definition.GetDeclaringType(), method)
            : null;
    }

    private static bool AddAll(HashSet<string> set, IEnumerable<string> items)
    {
        bool added = false;
        foreach (string item in items)
        {
            added |= set.Add(item);
        }

        return added;
    }

    /// <summary>
    /// The strings in each method's attribute arguments, by its outermost type and name. Read from the attribute blobs as
    /// length-prefixed UTF-8 runs, which may also pick up a stray run that is not a string: that only marks more.
    /// </summary>
    private static Dictionary<(TypeDefinitionHandle, string), List<string>> AttributeStrings(MetadataReader reader)
    {
        Dictionary<(TypeDefinitionHandle, string), List<string>> strings = [];
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            foreach (MethodDefinitionHandle methodHandle in reader.GetTypeDefinition(typeHandle).GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                List<string> found = [];
                foreach (CustomAttribute attribute in method.GetCustomAttributes().Select(reader.GetCustomAttribute))
                {
                    // The compiler's own (AsyncStateMachine(typeof(TrxTests+<Run>d__3))) name types, not paths.
                    if (!AttributeName(reader, attribute.Constructor).StartsWith("System.Runtime.CompilerServices.", StringComparison.Ordinal)
                        && !AttributeName(reader, attribute.Constructor).StartsWith("System.Diagnostics.", StringComparison.Ordinal))
                    {
                        found.AddRange(BlobStrings(reader.GetBlobBytes(attribute.Value)));
                    }
                }

                if (found.Count > 0)
                {
                    (TypeDefinitionHandle, string) key = (Outermost(reader, typeHandle), reader.GetString(method.Name));
                    if (!strings.TryGetValue(key, out List<string>? list))
                    {
                        strings[key] = list = [];
                    }

                    list.AddRange(found);
                }
            }
        }

        return strings;
    }

    private static IEnumerable<string> BlobStrings(byte[] blob)
    {
        for (int index = 0; index < blob.Length; index++)
        {
            int length = blob[index];
            if (length is < 2 or >= 0x80 || index + 1 + length > blob.Length)
            {
                continue;
            }

            string text;
            try
            {
                text = new System.Text.UTF8Encoding(false, true).GetString(blob, index + 1, length);
            }
            catch (System.Text.DecoderFallbackException)
            {
                continue;
            }

            if (!text.Any(char.IsControl))
            {
                yield return text;
            }
        }
    }

    /// <summary>
    /// The method of the outermost type whose attributes reach this body: its own name, or for a compiler-generated method or
    /// type (an async state machine <c>&lt;Run&gt;d__3</c>, a lambda <c>&lt;Run&gt;b__3_0</c>) the method it was generated from.
    /// </summary>
    private static string SourceMethod(MetadataReader reader, TypeDefinitionHandle type, MethodDefinition method)
    {
        string name = reader.GetString(method.Name);
        if (GeneratedFrom(name) is string fromMethod)
        {
            return fromMethod;
        }

        for (TypeDefinitionHandle current = type; !current.IsNil; current = reader.GetTypeDefinition(current).GetDeclaringType())
        {
            if (GeneratedFrom(reader.GetString(reader.GetTypeDefinition(current).Name)) is string fromType)
            {
                return fromType;
            }
        }

        return name;
    }

    private static string? GeneratedFrom(string name) =>
        name.StartsWith('<') && name.IndexOf('>', StringComparison.Ordinal) is > 1 and int close ? name[1..close] : null;

    /// <summary>A member of a generic type of the assembly (<c>Fixture&lt;T&gt;</c>) is named through its instantiation: the type it instantiates is touched.</summary>
    private static void TouchGeneric(MetadataReader reader, TamperingBody body, EntityHandle member)
    {
        if (member.Kind != HandleKind.MemberReference || reader.GetMemberReference((MemberReferenceHandle)member).Parent is not { Kind: HandleKind.TypeSpecification } parent)
        {
            return;
        }

        BlobReader signature = reader.GetBlobReader(reader.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
        if (signature.ReadSignatureTypeCode() == SignatureTypeCode.GenericTypeInstance)
        {
            signature.ReadByte();
            if (signature.ReadTypeHandle() is { Kind: HandleKind.TypeDefinition } definition)
            {
                body.Touched.Add(Outermost(reader, (TypeDefinitionHandle)definition));
            }
        }
    }

    private static bool IsTemp((string Type, string Member) call) =>
        call.Type is "System.IO.Path" or "System.IO.Directory" && TempMembers.Contains(call.Member, StringComparer.Ordinal);

    /// <summary>
    /// Whether the body names a folder of the run other than a temporary one: an environment variable (the CI's own files are named
    /// by variables), the current, base or process directory, or where an assembly was loaded from (next to the runner's results).
    /// </summary>
    private static bool NamesAnotherFolder(TamperingBody body) => NamesAnotherFolder(body.Calls);

    private static bool NamesAnotherFolder(List<(string Type, string Member)> calls) => calls.Any(call => call is ("System.Environment", "GetEnvironmentVariable" or "GetEnvironmentVariables" or "ExpandEnvironmentVariables" or "get_CurrentDirectory" or "get_ProcessPath")
        or ("System.IO.Directory", "GetCurrentDirectory") or ("System.AppContext", "get_BaseDirectory" or "GetData") or ("System.AppDomain", "get_BaseDirectory")
        or ("System.Reflection.Assembly", "get_Location" or "get_CodeBase"));

    /// <summary>Whether the body loads a string that names a runner's results or a path outside a folder it was given: <c>.trx</c>, <c>TestResults</c>, <c>junit</c>, <c>..</c>, a rooted path.</summary>
    private static bool NamesAResultsPath(TamperingBody body) => NamesAResultsPath(body.Strings) || NamesAResultsPath(body.AttributeStrings);

    private static bool NamesAResultsPath(List<string> strings) => strings.Any(text =>
        text.Contains(".trx", StringComparison.OrdinalIgnoreCase) || text.Contains("TestResults", StringComparison.OrdinalIgnoreCase)
        || text.Contains("junit", StringComparison.OrdinalIgnoreCase) || text.Contains("..", StringComparison.Ordinal)
        || text.StartsWith('/') || text.StartsWith('\\') || (text.Length >= 2 && char.IsAsciiLetter(text[0]) && text[1] == ':'));

    private static TypeDefinitionHandle Outermost(MetadataReader reader, TypeDefinitionHandle type)
    {
        while (reader.GetTypeDefinition(type).GetDeclaringType() is { IsNil: false } declaring)
        {
            type = declaring;
        }

        return type;
    }

    private static IEnumerable<TamperingFinding> Findings(
        TamperingBody body, HashSet<string> tempReach, HashSet<string> elsewhere, string assembly, string location)
    {
        List<(string Type, string Member)> calls = body.Calls;
        List<string> strings = body.Strings;
        foreach ((string type, string member) in calls)
        {
            foreach ((string callType, string[] members, string kind) in Calls)
            {
                if (type == callType && members.Contains(member, StringComparer.Ordinal))
                {
                    yield return new TamperingFinding(assembly, kind, location, $"{type}.{member}");
                }
            }
        }

        // A write counts as visibly temporary only when the method names no other folder of the run and no runner's results,
        // rooted or parent path, itself, in its attributes or through a member of the assembly it uses (Elsewhere), and asks for
        // the temporary folder before it, or uses a type of the assembly that asks for one (its own, or a fixture's). A path from
        // another assembly (a shared helper library) is not followed: a guardrail.
        int firstTemp = calls.FindIndex(IsTemp);
        bool namesAnotherFolder = elsewhere.Contains(body.Key) || NamesAnotherFolder(body) || NamesAResultsPath(body);
        bool usesATempOwner = body.Members.Overlaps(tempReach);
        foreach ((string type, string member) in calls.Where((call, index) => (namesAnotherFolder || ((firstTemp < 0 || index < firstTemp) && !usesATempOwner))
            && WriteTypes.Contains(call.Type, StringComparer.Ordinal)
            && WriteMembers.Any(prefix => call.Member.StartsWith(prefix, StringComparison.Ordinal)) && !call.Member.Contains("Read", StringComparison.Ordinal)))
        {
            yield return new TamperingFinding(assembly, "file-write", location, $"{type}.{member}");
        }

        if (strings.FirstOrDefault(text => RunnerNamespaces.Any(space => text.StartsWith(space, StringComparison.Ordinal))) is string runnerType
            && calls.FirstOrDefault(call => ReflectionMembers.Contains(call.Member, StringComparer.Ordinal) && call.Type.StartsWith("System.", StringComparison.Ordinal)) is { Member: not null } reflection)
        {
            yield return new TamperingFinding(assembly, "runner-reflection", location, $"{reflection.Type}.{reflection.Member}(\"{runnerType}\")");
        }

        if (calls.Any(call => call is ("System.Environment", "SetEnvironmentVariable"))
            && strings.FirstOrDefault(text => CiVariablePrefixes.Any(prefix => text.StartsWith(prefix, StringComparison.Ordinal))) is string variable)
        {
            yield return new TamperingFinding(assembly, "ci-environment", location, $"Environment.SetEnvironmentVariable(\"{variable}\")");
        }
    }

    /// <summary>The type and name of the member a token names; an empty type for one of this assembly (never a framework call).</summary>
    private static (string Type, string Member) Member(MetadataReader reader, EntityHandle token)
    {
        switch (token.Kind)
        {
            case HandleKind.MemberReference:
                MemberReference member = reader.GetMemberReference((MemberReferenceHandle)token);
                return (member.Parent.Kind == HandleKind.TypeReference ? ReferenceName(reader, (TypeReferenceHandle)member.Parent) : string.Empty, reader.GetString(member.Name));
            case HandleKind.MethodSpecification:
                return Member(reader, reader.GetMethodSpecification((MethodSpecificationHandle)token).Method);
            default:
                return (string.Empty, string.Empty);
        }
    }

    private static string AttributeName(MetadataReader reader, EntityHandle constructor) =>
        constructor.Kind == HandleKind.MemberReference && reader.GetMemberReference((MemberReferenceHandle)constructor).Parent is { Kind: HandleKind.TypeReference } parent
            ? ReferenceName(reader, (TypeReferenceHandle)parent)
            : string.Empty;

    private static string ReferenceName(MetadataReader reader, TypeReferenceHandle handle)
    {
        TypeReference type = reader.GetTypeReference(handle);
        return type.ResolutionScope.Kind == HandleKind.TypeReference
            ? $"{ReferenceName(reader, (TypeReferenceHandle)type.ResolutionScope)}+{reader.GetString(type.Name)}"
            : $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";
    }

    private static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        TypeDefinitionHandle declaring = type.GetDeclaringType();
        string name = reader.GetString(type.Name);
        if (!declaring.IsNil)
        {
            return $"{TypeName(reader, declaring)}+{name}";
        }

        string space = reader.GetString(type.Namespace);
        return space.Length == 0 ? name : $"{space}.{name}";
    }

    [GeneratedRegex(@"<([^<>]*)>[a-zA-Z]?__[0-9A-Za-z_|]+")]
    private static partial Regex GeneratedOrdinal();

    private static int OperandSize(byte code) => code switch
    {
        >= 0x0E and <= 0x13 => 1,
        0x1F => 1,
        0x20 or 0x22 => 4,
        0x21 or 0x23 => 8,
        0x29 => 4,
        >= 0x2B and <= 0x37 => 1,
        >= 0x38 and <= 0x44 => 4,
        >= 0x70 and <= 0x75 => 4,
        0x79 => 4,
        >= 0x7B and <= 0x81 => 4,
        0x8C or 0x8D or 0x8F => 4,
        >= 0xA3 and <= 0xA5 => 4,
        0xC2 or 0xC6 or 0xD0 => 4,
        0xDD => 4,
        0xDE => 1,
        _ => 0,
    };
}
