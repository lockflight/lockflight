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
                        bodies.Add((location, Read(reader, pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader(), Outermost(reader, typeHandle))));
                    }
                }
            }

            // The types of the assembly that ask for a temporary folder, by their outermost type (an async method's state machine
            // and a lambda's closure are nested types).
            // A type that also names another folder anywhere (an environment variable, the base directory) is no temporary-folder owner.
            HashSet<TypeDefinitionHandle> elsewhere = [.. bodies.Where(body => NamesAnotherFolder(body.Body)).Select(body => body.Body.Owner)];
            HashSet<TypeDefinitionHandle> tempOwners = [.. bodies.Where(body => body.Body.Calls.Any(IsTemp)).Select(body => body.Body.Owner).Where(owner => !elsewhere.Contains(owner))];
            foreach ((string location, TamperingBody body) in bodies)
            {
                findings.AddRange(Findings(body, tempOwners, assembly, location));
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

    private static TamperingBody Read(MetadataReader reader, BlobReader il, TypeDefinitionHandle owner)
    {
        TamperingBody body = new(owner, [], [], []);
        while (il.RemainingBytes > 0)
        {
            byte code = il.ReadByte();
            if (code == TwoBytePrefix)
            {
                byte second = il.ReadByte();
                if (TwoByteMethodOps.Contains(second))
                {
                    Call(reader, body, MetadataTokens.EntityHandle(il.ReadInt32()));
                }
                else
                {
                    il.Offset += second switch { >= 0x09 and <= 0x0E => 2, 0x12 or 0x19 => 1, 0x15 or 0x16 or 0x1C => 4, _ => 0 };
                }
            }
            else if (MethodOps.Contains(code))
            {
                Call(reader, body, MetadataTokens.EntityHandle(il.ReadInt32()));
            }
            else if (FieldLoadOps.Contains(code))
            {
                if (MetadataTokens.EntityHandle(il.ReadInt32()) is { Kind: HandleKind.FieldDefinition } field)
                {
                    body.Touched.Add(Outermost(reader, reader.GetFieldDefinition((FieldDefinitionHandle)field).GetDeclaringType()));
                }
            }
            else if (code == StringOpCode)
            {
                body.Strings.Add(reader.GetUserString(MetadataTokens.UserStringHandle(il.ReadInt32() & 0x00FFFFFF)));
            }
            else if (code == SwitchOpCode)
            {
                il.Offset += il.ReadInt32() * sizeof(int);
            }
            else
            {
                il.Offset += OperandSize(code);
            }
        }

        return body;
    }

    private static void Call(MetadataReader reader, TamperingBody body, EntityHandle token)
    {
        body.Calls.Add(Member(reader, token));
        EntityHandle method = token.Kind == HandleKind.MethodSpecification ? reader.GetMethodSpecification((MethodSpecificationHandle)token).Method : token;
        if (method.Kind == HandleKind.MethodDefinition)
        {
            body.Touched.Add(Outermost(reader, reader.GetMethodDefinition((MethodDefinitionHandle)method).GetDeclaringType()));
        }
    }

    private static bool IsTemp((string Type, string Member) call) =>
        call.Type is "System.IO.Path" or "System.IO.Directory" && TempMembers.Contains(call.Member, StringComparer.Ordinal);

    /// <summary>
    /// Whether the body names a folder of the run other than a temporary one: an environment variable (the CI's own files are named
    /// by variables), the current, base or process directory, or where an assembly was loaded from (next to the runner's results).
    /// </summary>
    private static bool NamesAnotherFolder(TamperingBody body) => body.Calls.Any(call => call is ("System.Environment", "GetEnvironmentVariable" or "GetEnvironmentVariables" or "ExpandEnvironmentVariables" or "get_CurrentDirectory" or "get_ProcessPath")
        or ("System.IO.Directory", "GetCurrentDirectory") or ("System.AppContext", "get_BaseDirectory") or ("System.AppDomain", "get_BaseDirectory")
        or ("System.Reflection.Assembly", "get_Location" or "get_CodeBase"));

    /// <summary>Whether the body loads a string that names a runner's results or a path outside a folder it was given: <c>.trx</c>, <c>TestResults</c>, <c>junit</c>, <c>..</c>, a rooted path.</summary>
    private static bool NamesAResultsPath(TamperingBody body) => body.Strings.Any(text =>
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

    private static IEnumerable<TamperingFinding> Findings(TamperingBody body, HashSet<TypeDefinitionHandle> tempOwners, string assembly, string location)
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

        // A write counts as visibly temporary only when the method names no other folder of the run, and asks for the temporary
        // folder before it, or uses a type of the assembly that asks for one and names no other folder (its own, or a fixture's)
        // while the method loads no string naming the runner's results or a rooted or parent path.
        int firstTemp = calls.FindIndex(IsTemp);
        bool namesAnotherFolder = NamesAnotherFolder(body);
        bool usesATempOwner = body.Touched.Overlaps(tempOwners) && !NamesAResultsPath(body);
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

    [GeneratedRegex(@"<([^<>]*)>[a-zA-Z]?__[0-9A-Za-z_]+")]
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
