using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// Reads a built test assembly without loading it (AOT-safe) and walks a test method's IL — and the IL of the test assembly's own
/// methods it reaches (helpers, lambdas, local functions, async and iterator state machines, base classes). It answers two questions.
/// <list type="bullet">
/// <item>Whether the test calls production code: calls, constructs or takes a delegate to a method declared in one of the production
/// assemblies. Static evidence that the test exercises production code; it does not prove that the failing assertion compares a
/// value the production code produced, which the review judges.</item>
/// <item>Whether the test asserts nothing: no call into an assertion library or to an assertion-named method, and no
/// <c>throw</c>. Production code is not read, so a test that relies on production code throwing looks assertion-free.</item>
/// </list>
/// </summary>
internal sealed class TestCodeScanner(string assemblyPath, IReadOnlySet<string> productionAssemblies) : IDisposable
{
    /// <summary>Bounds the walk through the test assembly's own methods; a test that reaches production code does so long before.</summary>
    private const int MaxMethodsVisited = 2000;

    /// <summary>Assemblies whose every method is an assertion or builds one (xUnit, NUnit, MSTest, FluentAssertions, Shouldly, Verify).</summary>
    private static readonly HashSet<string> AssertionAssemblies = new(
        [
            "xunit.assert", "xunit.v3.assert", "nunit.framework", "Microsoft.VisualStudio.TestPlatform.TestFramework", "MSTest.TestFramework",
            "FluentAssertions", "AwesomeAssertions", "Shouldly", "Verify", "Verify.Xunit", "Verify.XunitV3", "Verify.NUnit", "Verify.MSTest",
        ],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Method names that assert in a library outside the production code: <c>Debug.Assert</c>, a shared test library's <c>AssertValid</c>, Moq's <c>Verify</c>,
    /// NSubstitute's <c>Received</c> and <c>DidNotReceive</c>, FakeItEasy's <c>MustHaveHappened</c>, an extension's <c>ShouldBe</c>.
    /// </summary>
    private static readonly string[] AssertionMethodPrefixes = ["Assert", "Verify", "Should", "Received", "DidNotReceive", "MustHave", "MustNotHave"];

    private readonly PEReader pe = new(File.OpenRead(assemblyPath));

    public bool CallsProduction(string className, string method) =>
        Walk(className, method, IsProduction, throwFinds: false) == CodeWalkOutcome.Found;

    /// <summary>True only when the whole walk ran and found neither an assertion nor a <c>throw</c>; false when the test cannot be found.</summary>
    public bool AssertsNothing(string className, string method) =>
        Walk(className, method, IsAssertion, throwFinds: true) == CodeWalkOutcome.NotFound;

    private CodeWalkOutcome Walk(string className, string method, Func<MetadataReader, EntityHandle, bool> finds, bool throwFinds)
    {
        MetadataReader reader = pe.GetMetadataReader();
        if (FindType(reader, className) is not TypeDefinitionHandle type)
        {
            return CodeWalkOutcome.Undetermined;
        }

        Queue<MethodDefinitionHandle> pending = new(MethodsNamed(reader, type, method, includeBases: true));
        if (pending.Count == 0)
        {
            return CodeWalkOutcome.Undetermined;
        }

        HashSet<MethodDefinitionHandle> visited = [];
        while (pending.Count > 0)
        {
            if (visited.Count >= MaxMethodsVisited)
            {
                return CodeWalkOutcome.Undetermined;
            }

            MethodDefinitionHandle handle = pending.Dequeue();
            if (!visited.Add(handle))
            {
                continue;
            }

            MethodDefinition definition = reader.GetMethodDefinition(handle);
            foreach (MethodDefinitionHandle stateMachineMethod in StateMachineMethods(reader, definition))
            {
                pending.Enqueue(stateMachineMethod);
            }

            if (definition.RelativeVirtualAddress == 0)
            {
                continue;
            }

            IlBody body = IlInstructions.Read(pe.GetMethodBody(definition.RelativeVirtualAddress).GetILReader());
            if (throwFinds && body.Throws)
            {
                return CodeWalkOutcome.Found;
            }

            foreach (EntityHandle token in body.MethodTokens)
            {
                if (finds(reader, token))
                {
                    return CodeWalkOutcome.Found;
                }

                foreach (MethodDefinitionHandle own in OwnMethods(reader, token))
                {
                    pending.Enqueue(own);
                }
            }
        }

        return CodeWalkOutcome.NotFound;
    }

    public void Dispose() => pe.Dispose();

    /// <summary>The type <c>Namespace.Outer+Inner</c> as the test inventory names it.</summary>
    private static TypeDefinitionHandle? FindType(MetadataReader reader, string className)
    {
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            if (FullName(reader, handle) == className)
            {
                return handle;
            }
        }

        return null;
    }

    private static string FullName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);
        TypeDefinitionHandle declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return $"{FullName(reader, declaring)}+{name}";
        }

        string space = reader.GetString(type.Namespace);
        return space.Length == 0 ? name : $"{space}.{name}";
    }

    private static IEnumerable<MethodDefinitionHandle> MethodsNamed(MetadataReader reader, TypeDefinitionHandle type, string name, bool includeBases)
    {
        for (TypeDefinitionHandle? current = type; current is TypeDefinitionHandle currentHandle; current = includeBases ? BaseInSameAssembly(reader, currentHandle) : null)
        {
            foreach (MethodDefinitionHandle handle in reader.GetTypeDefinition(currentHandle).GetMethods())
            {
                if (reader.GetString(reader.GetMethodDefinition(handle).Name) == name)
                {
                    yield return handle;
                }
            }
        }
    }

    private static TypeDefinitionHandle? BaseInSameAssembly(MetadataReader reader, TypeDefinitionHandle handle)
    {
        EntityHandle baseType = reader.GetTypeDefinition(handle).BaseType;
        return !baseType.IsNil && baseType.Kind == HandleKind.TypeDefinition ? (TypeDefinitionHandle)baseType : null;
    }

    /// <summary>
    /// The compiler moves the body of an async or iterator method (or lambda) into a nested state machine type named
    /// <c>&lt;Method&gt;d__N</c> (<c>&lt;&lt;Method&gt;b__0_0&gt;d</c> for a lambda); the method itself only starts it.
    /// </summary>
    private static IEnumerable<MethodDefinitionHandle> StateMachineMethods(MetadataReader reader, MethodDefinition method)
    {
        string prefix = $"<{reader.GetString(method.Name)}>d";
        foreach (TypeDefinitionHandle nested in reader.GetTypeDefinition(method.GetDeclaringType()).GetNestedTypes())
        {
            if (reader.GetString(reader.GetTypeDefinition(nested).Name).StartsWith(prefix, StringComparison.Ordinal))
            {
                foreach (MethodDefinitionHandle handle in reader.GetTypeDefinition(nested).GetMethods())
                {
                    yield return handle;
                }
            }
        }
    }

    /// <summary>Methods of the test assembly itself that a token names, so the walk follows helpers and generic test types.</summary>
    private static IEnumerable<MethodDefinitionHandle> OwnMethods(MetadataReader reader, EntityHandle token)
    {
        switch (token.Kind)
        {
            case HandleKind.MethodDefinition:
                return [(MethodDefinitionHandle)token];
            case HandleKind.MethodSpecification:
                return OwnMethods(reader, reader.GetMethodSpecification((MethodSpecificationHandle)token).Method);
            case HandleKind.MemberReference:
                MemberReference member = reader.GetMemberReference((MemberReferenceHandle)token);
                return member.Parent.Kind == HandleKind.TypeSpecification && GenericDefinition(reader, (TypeSpecificationHandle)member.Parent) is TypeDefinitionHandle own
                    ? MethodsNamed(reader, own, reader.GetString(member.Name), includeBases: false)
                    : [];
            default:
                return [];
        }
    }

    private bool IsProduction(MetadataReader reader, EntityHandle token) => token.Kind switch
    {
        HandleKind.MethodSpecification => IsProduction(reader, reader.GetMethodSpecification((MethodSpecificationHandle)token).Method),
        HandleKind.MemberReference => AssemblyOf(reader, reader.GetMemberReference((MemberReferenceHandle)token).Parent) is string assembly && productionAssemblies.Contains(assembly),
        _ => false,
    };

    /// <summary>A call into an assertion library, or to a method of another non-production assembly whose name says it asserts.</summary>
    private bool IsAssertion(MetadataReader reader, EntityHandle token)
    {
        switch (token.Kind)
        {
            case HandleKind.MethodSpecification:
                return IsAssertion(reader, reader.GetMethodSpecification((MethodSpecificationHandle)token).Method);
            case HandleKind.MemberReference:
                MemberReference member = reader.GetMemberReference((MemberReferenceHandle)token);
                if (AssemblyOf(reader, member.Parent) is not string assembly)
                {
                    return false;
                }

                string name = reader.GetString(member.Name);
                return AssertionAssemblies.Contains(assembly)
                    || (!productionAssemblies.Contains(assembly) && AssertionMethodPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)));
            default:
                return false;
        }
    }

    /// <summary>The name of the assembly a referenced type lives in; null for the test assembly itself or a shape that is not a type.</summary>
    private static string? AssemblyOf(MetadataReader reader, EntityHandle type)
    {
        switch (type.Kind)
        {
            case HandleKind.TypeReference:
                EntityHandle scope = reader.GetTypeReference((TypeReferenceHandle)type).ResolutionScope;
                return scope.Kind switch
                {
                    HandleKind.AssemblyReference => reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name),
                    HandleKind.TypeReference => AssemblyOf(reader, scope),
                    _ => null,
                };
            case HandleKind.TypeSpecification:
                return GenericBase(reader, (TypeSpecificationHandle)type) is EntityHandle generic ? AssemblyOf(reader, generic) : null;
            default:
                return null;
        }
    }

    private static TypeDefinitionHandle? GenericDefinition(MetadataReader reader, TypeSpecificationHandle specification) =>
        GenericBase(reader, specification) is EntityHandle { Kind: HandleKind.TypeDefinition } definition ? (TypeDefinitionHandle)definition : null;

    /// <summary>The generic type of a constructed one (<c>Repository&lt;Order&gt;</c> → <c>Repository`1</c>); null for arrays, pointers and the like.</summary>
    private static EntityHandle? GenericBase(MetadataReader reader, TypeSpecificationHandle specification)
    {
        BlobReader blob = reader.GetBlobReader(reader.GetTypeSpecification(specification).Signature);
        if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
        {
            return null;
        }

        blob.ReadSignatureTypeCode();
        return blob.ReadTypeHandle();
    }
}
