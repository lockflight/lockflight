using System.Reflection.Metadata;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>Resolves a type reference of a test assembly to the type it defines, through a generic instantiation.</summary>
internal static class GenericTypes
{
    /// <summary>
    /// The type this assembly defines that <paramref name="type"/> names: itself for a definition, the generic definition of a
    /// constructed one (<c>PriceTestsBase&lt;decimal&gt;</c> → <c>PriceTestsBase`1</c>); null for nil, another assembly's type, an
    /// array or the like.
    /// </summary>
    public static TypeDefinitionHandle? Definition(MetadataReader reader, EntityHandle type)
    {
        if (type.IsNil)
        {
            return null;
        }

        if (type.Kind == HandleKind.TypeDefinition)
        {
            return (TypeDefinitionHandle)type;
        }

        if (type.Kind != HandleKind.TypeSpecification)
        {
            return null;
        }

        BlobReader blob = reader.GetBlobReader(reader.GetTypeSpecification((TypeSpecificationHandle)type).Signature);
        if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
        {
            return null;
        }

        blob.ReadSignatureTypeCode();
        return blob.ReadTypeHandle() is { Kind: HandleKind.TypeDefinition } definition ? (TypeDefinitionHandle)definition : null;
    }
}
