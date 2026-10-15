using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Lockflight.Cli.Testing;

/// <summary>
/// The xUnit v2 tests of a built assembly, read from its metadata without loading it (AOT-safe): public methods of non-abstract
/// classes — inherited ones from base classes in the same assembly too — whose attribute is <c>FactAttribute</c>,
/// <c>TheoryAttribute</c> or one derived by name (<c>SkippableFactAttribute</c>), with their <c>Skip</c> reason and
/// <c>[Trait("Requirement", …)]</c> values. VSTest's own listing has neither assembly nor traits.
/// </summary>
internal static class AssemblyTestMetadata
{
    private const string RequirementTrait = "Requirement";

    public static IReadOnlyList<TestCase> Read(string assemblyPath)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        using PEReader pe = new(stream);
        MetadataReader reader = pe.GetMetadataReader();
        string assembly = Path.GetFileNameWithoutExtension(assemblyPath);
        List<TestCase> tests = [];
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            TypeAttributes visibility = type.Attributes & TypeAttributes.VisibilityMask;
            if ((type.Attributes & TypeAttributes.Abstract) != 0 || (type.Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Interface
                || visibility is not (TypeAttributes.Public or TypeAttributes.NestedPublic))
            {
                // xUnit v2 runs public, non-abstract classes only.
                continue;
            }

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

                    (bool isTest, string? skip, IReadOnlyList<string> requirements) = TestAttributes(reader, method);
                    if (isTest && seen.Add(name))
                    {
                        tests.Add(new TestCase(assembly, className, name, requirements, skip));
                    }
                }
            }
        }

        return tests;
    }

    /// <summary>Whether a method is a test, its skip reason and its Requirement trait values.</summary>
    private static (bool IsTest, string? Skip, IReadOnlyList<string> Requirements) TestAttributes(MetadataReader reader, MethodDefinition method)
    {
        bool isTest = false;
        string? skip = null;
        List<string> requirements = [];
        foreach (CustomAttributeHandle handle in method.GetCustomAttributes())
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            string attributeType = AttributeTypeName(reader, attribute);
            if (attributeType.EndsWith("FactAttribute", StringComparison.Ordinal) || attributeType.EndsWith("TheoryAttribute", StringComparison.Ordinal))
            {
                isTest = true;
                CustomAttributeValue<string> value = attribute.DecodeValue(StringTypes.Instance);
                skip = value.NamedArguments.FirstOrDefault(argument => argument.Name == "Skip").Value as string ?? skip;
            }
            else if (attributeType == "TraitAttribute")
            {
                ImmutableArray<CustomAttributeTypedArgument<string>> arguments = attribute.DecodeValue(StringTypes.Instance).FixedArguments;
                if (arguments.Length == 2 && arguments[0].Value as string == RequirementTrait && arguments[1].Value is string requirement)
                {
                    requirements.Add(requirement);
                }
            }
        }

        return (isTest, skip, requirements);
    }

    private static string AttributeTypeName(MetadataReader reader, CustomAttribute attribute) => attribute.Constructor.Kind switch
    {
        HandleKind.MemberReference => TypeName(reader, reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent),
        HandleKind.MethodDefinition => reader.GetString(reader.GetTypeDefinition(reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType()).Name),
        _ => string.Empty,
    };

    private static string TypeName(MetadataReader reader, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)handle).Name),
        HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)handle).Name),
        _ => string.Empty,
    };

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

    private static TypeDefinitionHandle? BaseInSameAssembly(MetadataReader reader, TypeDefinitionHandle handle)
    {
        EntityHandle baseType = reader.GetTypeDefinition(handle).BaseType;
        return !baseType.IsNil && baseType.Kind == HandleKind.TypeDefinition ? (TypeDefinitionHandle)baseType : null;
    }

    /// <summary>Decodes attribute arguments far enough to read strings; every other type is named, not resolved.</summary>
    private sealed class StringTypes : ICustomAttributeTypeProvider<string>
    {
        public static StringTypes Instance { get; } = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetSystemType() => "System.Type";

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeDefinition(handle).Name);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeReference(handle).Name);

        public string GetTypeFromSerializedName(string name) => name;

        // Enum arguments are not needed (Fact, Theory and Trait take strings); a 32-bit underlying type is the common case.
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;

        public bool IsSystemType(string type) => type is "System.Type" or "Type";
    }
}
