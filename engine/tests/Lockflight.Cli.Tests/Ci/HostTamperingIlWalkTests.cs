using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Lockflight.Cli.Ci;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// The scanner's IL walker on hand-assembled IL: a <c>switch</c> is skipped with its whole jump table, so the instruction after
/// it is read as code. Skipping four bytes short (the bug fixed in #215) reads the table's last entry as an <c>ldc.i4</c> whose
/// operand swallows the <c>ldstr</c> after it; the walk then misreads the string's token as code and runs off the body.
/// </summary>
public sealed class HostTamperingIlWalkTests
{
    private const string Landed = "landed after the jump table";

    [Fact]
    public void TheWalkerSkipsASwitchsWholeJumpTableAndReadsTheNextInstruction()
    {
        MetadataBuilder metadata = new();
        metadata.AddModule(0, metadata.GetOrAddString("Il.dll"), metadata.GetOrAddGuid(Guid.Empty), default, default);
        int token = MetadataTokens.GetToken(metadata.GetOrAddUserString(Landed));
        BlobBuilder il = new();
        il.WriteByte(0x45); // switch
        il.WriteInt32(2); // two targets
        il.WriteInt32(0);
        il.WriteBytes(new byte[] { 0x20, 0x00, 0x00, 0x00 }); // the last target, which reads as ldc.i4 when taken for code
        il.WriteByte(0x72); // ldstr
        il.WriteInt32(token);
        il.WriteByte(0x2A); // ret
        BlobHandle body = metadata.GetOrAddBlob(il);
        BlobBuilder image = new();
        new MetadataRootBuilder(metadata).Serialize(image, 0, 0);
        using MetadataReaderProvider provider = MetadataReaderProvider.FromMetadataImage([.. image.ToArray()]);
        MetadataReader reader = provider.GetMetadataReader();

        TamperingBody walked = HostTamperingScanner.Read(reader, reader.GetBlobReader(body), default, "Il::Method", constructor: false);

        Assert.Equal([Landed], walked.Strings);
    }
}
