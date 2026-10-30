using System.Reflection.Metadata;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Type names for decoding custom attribute values (<see cref="TestFingerprinter"/>), as <see cref="SignatureNames"/> gives them for
/// signatures. An enum's underlying type cannot be read from another assembly, so it is taken to be <c>int</c>, as nearly every enum
/// is; a value that does not fit fails the decode, which gives no fingerprint (counts as changed).
/// </summary>
internal sealed class AttributeTypes(SignatureNames names) : ICustomAttributeTypeProvider<string>
{
    private const string SystemType = "System.Type";

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => names.GetPrimitiveType(typeCode);

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => names.GetTypeFromDefinition(reader, handle, rawTypeKind);

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => names.GetTypeFromReference(reader, handle, rawTypeKind);

    public string GetSZArrayType(string elementType) => names.GetSZArrayType(elementType);

    public string GetSystemType() => SystemType;

    public bool IsSystemType(string type) => type == SystemType || type.EndsWith("]System.Type", StringComparison.Ordinal);

    public string GetTypeFromSerializedName(string name) => name;

    public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
}
