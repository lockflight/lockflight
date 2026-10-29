using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace Lockflight.Cli.Ci;

/// <summary>
/// Decodes metadata signatures into stable text for <see cref="TestFingerprinter"/>: every type by its name (and, outside the
/// repository, its assembly and version) instead of the row numbers a signature blob holds, which shift whenever a type is added.
/// </summary>
internal sealed class SignatureNames(Func<TypeDefinitionHandle, string> definitionName, Func<TypeReferenceHandle, string> referenceName, MetadataReader reader)
    : ISignatureTypeProvider<string, object?>
{
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

    public string GetTypeFromDefinition(MetadataReader metadata, TypeDefinitionHandle handle, byte rawTypeKind) => definitionName(handle);

    public string GetTypeFromReference(MetadataReader metadata, TypeReferenceHandle handle, byte rawTypeKind) => referenceName(handle);

    public string GetTypeFromSpecification(MetadataReader metadata, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetSZArrayType(string elementType) => $"{elementType}[]";

    public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', shape.Rank - 1)}]";

    public string GetByReferenceType(string elementType) => $"{elementType}&";

    public string GetPointerType(string elementType) => $"{elementType}*";

    public string GetPinnedType(string elementType) => $"{elementType} pinned";

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => $"{genericType}<{string.Join(',', typeArguments)}>";

    public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";

    public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => $"{unmodifiedType} {(isRequired ? "modreq" : "modopt")}({modifier})";

    public string GetFunctionPointerType(MethodSignature<string> signature) => $"method {signature.ReturnType}({string.Join(',', signature.ParameterTypes)})";
}
