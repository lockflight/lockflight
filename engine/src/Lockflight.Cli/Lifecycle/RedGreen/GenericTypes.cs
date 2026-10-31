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

    /// <summary>
    /// Every type this assembly defines that a constructed type names, at any depth (<c>Base&lt;Calculator&gt;</c> → <c>Base`1</c>,
    /// <c>Calculator</c>; <c>Collection&lt;List&lt;Db&gt;&gt;</c> → <c>Db</c> and the rest); empty for a definition, a reference or a
    /// signature it cannot read.
    /// </summary>
    public static IReadOnlyList<TypeDefinitionHandle> OwnTypes(MetadataReader reader, EntityHandle type)
    {
        List<TypeDefinitionHandle> found = [];
        if (type.Kind != HandleKind.TypeSpecification)
        {
            return found;
        }

        BlobReader blob = reader.GetBlobReader(reader.GetTypeSpecification((TypeSpecificationHandle)type).Signature);
        try
        {
            Collect(ref blob, found);
        }
        catch (BadImageFormatException)
        {
            // What was read before the signature stopped making sense still counts.
        }

        return found;
    }

    private static void Collect(ref BlobReader blob, List<TypeDefinitionHandle> found)
    {
        switch (blob.ReadSignatureTypeCode())
        {
            case SignatureTypeCode.TypeHandle:
                if (blob.ReadTypeHandle() is { Kind: HandleKind.TypeDefinition } definition)
                {
                    found.Add((TypeDefinitionHandle)definition);
                }

                break;
            case SignatureTypeCode.GenericTypeInstance:
                Collect(ref blob, found);
                int count = blob.ReadCompressedInteger();
                for (int index = 0; index < count; index++)
                {
                    Collect(ref blob, found);
                }

                break;
            case SignatureTypeCode.SZArray or SignatureTypeCode.Pointer or SignatureTypeCode.ByReference or SignatureTypeCode.Pinned:
                Collect(ref blob, found);
                break;
            case SignatureTypeCode.Array:
                Collect(ref blob, found);
                blob.ReadCompressedInteger();
                int sizes = blob.ReadCompressedInteger();
                for (int index = 0; index < sizes; index++)
                {
                    blob.ReadCompressedInteger();
                }

                int bounds = blob.ReadCompressedInteger();
                for (int index = 0; index < bounds; index++)
                {
                    blob.ReadCompressedSignedInteger();
                }

                break;
            case SignatureTypeCode.RequiredModifier or SignatureTypeCode.OptionalModifier:
                blob.ReadTypeHandle();
                Collect(ref blob, found);
                break;
            case SignatureTypeCode.GenericTypeParameter or SignatureTypeCode.GenericMethodParameter:
                blob.ReadCompressedInteger();
                break;
            case SignatureTypeCode.FunctionPointer:
                throw new BadImageFormatException("a function pointer type argument is not followed");
            default:
                break;
        }
    }
}
