using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace Lockflight.Cli.Testing;

/// <summary>
/// Reads the test attributes of one framework from metadata, without loading the assembly (AOT-safe). xUnit v2 attributes match
/// by name in any namespace (<c>SkippableFactAttribute</c> derives from <c>FactAttribute</c>), as do MSTest's <c>…TestMethodAttribute</c>
/// and <c>…TestClassAttribute</c> (custom test attributes derive from them); NUnit's and MSTest's other attributes only in their own
/// namespace. A requirement is a <c>Requirement</c> trait or property with a string value; a category (NUnit's <c>[Category]</c>,
/// MSTest's <c>[TestCategory]</c>) is not one, as it has no name to say what the value is, but it is a trait a filter can name.
/// </summary>
internal static class TestAttributes
{
    private const string RequirementName = "Requirement";

    private const string NUnitNamespace = "NUnit.Framework";

    private const string MSTestNamespace = "Microsoft.VisualStudio.TestTools.UnitTesting";

    private static readonly HashSet<string> NUnitTestAttributes = new(["TestAttribute", "TestCaseAttribute", "TestCaseSourceAttribute", "TheoryAttribute"], StringComparer.Ordinal);

    public static TestAttributeSet OfMethod(MetadataReader reader, CustomAttributeHandleCollection attributes, TestFrameworkKind framework) =>
        Read(reader, attributes, framework);

    /// <summary>
    /// A class's skip, explicit mark, requirements and traits, which apply to every test in it; of an xUnit v2 class only its
    /// traits are read (its requirements as before: from the methods).
    /// </summary>
    public static TestAttributeSet OfClass(MetadataReader reader, CustomAttributeHandleCollection attributes, TestFrameworkKind framework) =>
        framework == TestFrameworkKind.XunitV2
            ? TestAttributeSet.None with { Traits = Read(reader, attributes, framework).Traits }
            : Read(reader, attributes, framework) with { IsTest = false };

    /// <summary>An MSTest <c>[TestClass]</c>: MSTest runs the test methods of such classes only.</summary>
    public static bool IsMSTestClass(MetadataReader reader, CustomAttributeHandleCollection attributes) =>
        attributes.Any(handle => TypeOf(reader, reader.GetCustomAttribute(handle)).Name.EndsWith("TestClassAttribute", StringComparison.Ordinal));

    private static TestAttributeSet Read(MetadataReader reader, CustomAttributeHandleCollection attributes, TestFrameworkKind framework)
    {
        bool isTest = false;
        bool isExplicit = false;
        string? skip = null;
        List<string> requirements = [];
        List<KeyValuePair<string, string>> traits = [];
        foreach (CustomAttributeHandle handle in attributes)
        {
            CustomAttribute attribute = reader.GetCustomAttribute(handle);
            (string space, string name) = TypeOf(reader, attribute);
            switch (Meaning(framework, space, name))
            {
                case TestAttributeMeaning.Test:
                    isTest = true;
                    if (framework == TestFrameworkKind.XunitV2)
                    {
                        skip = attribute.DecodeValue(StringTypes.Instance).NamedArguments.FirstOrDefault(argument => argument.Name == "Skip").Value as string ?? skip;
                    }

                    break;
                case TestAttributeMeaning.Skip:
                    ImmutableArray<CustomAttributeTypedArgument<string>> reason = attribute.DecodeValue(StringTypes.Instance).FixedArguments;
                    skip = reason.Length > 0 && reason[0].Value is string text ? text : string.Empty;
                    break;
                case TestAttributeMeaning.Explicit:
                    isExplicit = true;
                    break;
                case TestAttributeMeaning.NamedTrait:
                    ImmutableArray<CustomAttributeTypedArgument<string>> arguments = attribute.DecodeValue(StringTypes.Instance).FixedArguments;
                    if (arguments.Length == 2 && arguments[0].Value is string traitName && arguments[1].Value is string traitValue)
                    {
                        traits.Add(KeyValuePair.Create(traitName, traitValue));
                        if (traitName == RequirementName)
                        {
                            requirements.Add(traitValue);
                        }
                    }

                    break;
                case TestAttributeMeaning.Category:
                    ImmutableArray<CustomAttributeTypedArgument<string>> category = attribute.DecodeValue(StringTypes.Instance).FixedArguments;
                    if (category.Length == 1 && category[0].Value is string categoryName)
                    {
                        traits.AddRange(CategoryTraits(framework).Select(name => KeyValuePair.Create(name, categoryName)));
                    }

                    break;
                case TestAttributeMeaning.None:
                    break;
            }
        }

        return new TestAttributeSet(isTest, skip, isExplicit, requirements, traits);
    }

    private static TestAttributeMeaning Meaning(TestFrameworkKind framework, string space, string name) => framework switch
    {
        TestFrameworkKind.XunitV2 when name.EndsWith("FactAttribute", StringComparison.Ordinal) || name.EndsWith("TheoryAttribute", StringComparison.Ordinal) => TestAttributeMeaning.Test,
        TestFrameworkKind.XunitV2 when name == "TraitAttribute" => TestAttributeMeaning.NamedTrait,
        TestFrameworkKind.NUnit when space != NUnitNamespace => TestAttributeMeaning.None,
        TestFrameworkKind.NUnit when NUnitTestAttributes.Contains(name) => TestAttributeMeaning.Test,
        TestFrameworkKind.NUnit when name == "IgnoreAttribute" => TestAttributeMeaning.Skip,
        TestFrameworkKind.NUnit when name == "ExplicitAttribute" => TestAttributeMeaning.Explicit,
        TestFrameworkKind.NUnit when name == "PropertyAttribute" => TestAttributeMeaning.NamedTrait,
        TestFrameworkKind.NUnit when name == "CategoryAttribute" => TestAttributeMeaning.Category,
        TestFrameworkKind.MSTest when name.EndsWith("TestMethodAttribute", StringComparison.Ordinal) => TestAttributeMeaning.Test,
        TestFrameworkKind.MSTest when space == MSTestNamespace && name == "IgnoreAttribute" => TestAttributeMeaning.Skip,
        TestFrameworkKind.MSTest when space == MSTestNamespace && name == "TestPropertyAttribute" => TestAttributeMeaning.NamedTrait,
        TestFrameworkKind.MSTest when space == MSTestNamespace && name == "TestCategoryAttribute" => TestAttributeMeaning.Category,
        _ => TestAttributeMeaning.None,
    };

    /// <summary>The trait names a category goes by in a VSTest filter: NUnit's adapter takes <c>Category</c> and <c>TestCategory</c>, MSTest's <c>TestCategory</c>.</summary>
    private static string[] CategoryTraits(TestFrameworkKind framework) => framework == TestFrameworkKind.NUnit ? ["Category", "TestCategory"] : ["TestCategory"];

    /// <summary>The namespace and name of an attribute's type; a nested type reference has no namespace of its own.</summary>
    private static (string Namespace, string Name) TypeOf(MetadataReader reader, CustomAttribute attribute)
    {
        EntityHandle type = attribute.Constructor.Kind switch
        {
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
            _ => default,
        };
        return type.Kind switch
        {
            HandleKind.TypeReference when reader.GetTypeReference((TypeReferenceHandle)type) is TypeReference reference => (reader.GetString(reference.Namespace), reader.GetString(reference.Name)),
            HandleKind.TypeDefinition when reader.GetTypeDefinition((TypeDefinitionHandle)type) is TypeDefinition definition => (reader.GetString(definition.Namespace), reader.GetString(definition.Name)),
            _ => (string.Empty, string.Empty),
        };
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

        // Enum arguments are not needed (the attributes read take strings); a 32-bit underlying type is the common case.
        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;

        public bool IsSystemType(string type) => type is "System.Type" or "Type";
    }
}
