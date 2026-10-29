using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Lockflight.Cli.Testing;

/// <summary>
/// The tests of a built xUnit v2 or NUnit assembly, read from its metadata without loading it (AOT-safe): public methods, inherited
/// ones from base classes in the same assembly too, carrying the framework's test attributes (<see cref="TestAttributes"/>), with
/// their skip reason, explicit mark and <c>Requirement</c> values. The classes are those the framework runs: for xUnit v2
/// non-abstract classes visible outside the assembly; for NUnit non-abstract or static classes of any visibility, generic ones left
/// out. VSTest's own listing has neither assembly nor traits. A test is one case per method: parameterized rows
/// (<c>[TestCase]</c>) are not enumerated.
/// </summary>
internal static class AssemblyTestMetadata
{
    /// <summary>The xUnit v2 tests of the assembly.</summary>
    public static IReadOnlyList<TestCase> Read(string assemblyPath) => Read(assemblyPath, TestFrameworkKind.XunitV2);

    public static IReadOnlyList<TestCase> Read(string assemblyPath, TestFrameworkKind framework)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        using PEReader pe = new(stream);
        MetadataReader reader = pe.GetMetadataReader();
        string assembly = Path.GetFileNameWithoutExtension(assemblyPath);
        List<TestCase> tests = [];
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            if (!IsTestClass(reader, handle, framework))
            {
                continue;
            }

            TestAttributeSet fixture = TestAttributes.OfClass(reader, reader.GetTypeDefinition(handle).GetCustomAttributes(), framework);
            string className = FullName(reader, handle);
            HashSet<string> seen = new(StringComparer.Ordinal);
            for (TypeDefinitionHandle? current = handle; current is TypeDefinitionHandle currentHandle; current = BaseInSameAssembly(reader, currentHandle))
            {
                foreach (MethodDefinitionHandle methodHandle in reader.GetTypeDefinition(currentHandle).GetMethods())
                {
                    MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                    string name = reader.GetString(method.Name);
                    if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public || seen.Contains(name))
                    {
                        continue;
                    }

                    TestAttributeSet attributes = TestAttributes.OfMethod(reader, method.GetCustomAttributes(), framework);
                    if (attributes.IsTest && seen.Add(name))
                    {
                        tests.Add(new TestCase(
                            assembly,
                            className,
                            name,
                            [.. fixture.Requirements.Concat(attributes.Requirements).Distinct(StringComparer.Ordinal)],
                            attributes.SkipReason ?? fixture.SkipReason,
                            attributes.IsExplicit || fixture.IsExplicit));
                    }
                }
            }
        }

        return tests;
    }

    /// <summary>Whether the framework runs the tests of a class (see the class summary for each framework's rule).</summary>
    private static bool IsTestClass(MetadataReader reader, TypeDefinitionHandle handle, TestFrameworkKind framework)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        if ((type.Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Interface)
        {
            return false;
        }

        bool isAbstract = (type.Attributes & TypeAttributes.Abstract) != 0;
        return framework switch
        {
            // A static class is abstract and sealed in metadata; NUnit runs its static tests.
            TestFrameworkKind.NUnit => (!isAbstract || (type.Attributes & TypeAttributes.Sealed) != 0) && type.GetGenericParameters().Count == 0,
            // xUnit v2 runs public, non-abstract classes only (its discovery reads the assembly's exported types).
            _ => !isAbstract && IsVisibleOutsideAssembly(reader, handle),
        };
    }

    /// <summary>A public class, or a public class nested only in public classes: what <c>Assembly.GetExportedTypes</c> lists.</summary>
    private static bool IsVisibleOutsideAssembly(MetadataReader reader, TypeDefinitionHandle handle)
    {
        TypeDefinition type = reader.GetTypeDefinition(handle);
        TypeDefinitionHandle declaring = type.GetDeclaringType();
        return (type.Attributes & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.Public => true,
            TypeAttributes.NestedPublic => !declaring.IsNil && IsVisibleOutsideAssembly(reader, declaring),
            _ => false,
        };
    }

    /// <summary><c>Namespace.Outer+Inner</c>, as VSTest's fully qualified names and xUnit write nested classes.</summary>
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

    /// <summary>The base class when this assembly defines it, a generic base (<c>Base&lt;int&gt;</c>) included; null otherwise.</summary>
    private static TypeDefinitionHandle? BaseInSameAssembly(MetadataReader reader, TypeDefinitionHandle handle)
    {
        EntityHandle baseType = reader.GetTypeDefinition(handle).BaseType;
        if (!baseType.IsNil && baseType.Kind == HandleKind.TypeSpecification)
        {
            baseType = GenericDefinition(reader, (TypeSpecificationHandle)baseType);
        }

        return !baseType.IsNil && baseType.Kind == HandleKind.TypeDefinition ? (TypeDefinitionHandle)baseType : null;
    }

    /// <summary>The generic type a <c>GENERICINST</c> type specification instantiates; a nil handle for any other specification.</summary>
    private static EntityHandle GenericDefinition(MetadataReader reader, TypeSpecificationHandle handle)
    {
        BlobReader signature = reader.GetBlobReader(reader.GetTypeSpecification(handle).Signature);
        if (signature.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
        {
            return default;
        }

        // CLASS or VALUETYPE, then the generic type itself.
        signature.ReadSignatureTypeCode();
        return signature.ReadTypeHandle();
    }
}
