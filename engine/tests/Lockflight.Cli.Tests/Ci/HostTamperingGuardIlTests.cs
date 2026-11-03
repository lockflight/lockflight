using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using Lockflight.Cli.Ci;

namespace Lockflight.Cli.Tests.Ci;

/// <summary>
/// The record-mode guard (<see cref="SnapshotRecords"/>) on hand-assembled IL, for shapes an optimized build gives and this test
/// project's own Debug build does not: a guard counts only when nothing outside it jumps into the block it guards. A guard is 22
/// bytes and a case-file write 15.
/// </summary>
public sealed class HostTamperingGuardIlTests
{
    private const byte Nop = 0x00;
    private const byte LoadArgument1 = 0x03;
    private const byte LoadOne = 0x17;
    private const byte Return = 0x2A;
    private const byte ShortBranch = 0x2B;
    private const byte ShortBranchFalse = 0x2C;
    private const byte ShortBranchTrue = 0x2D;
    private const byte LoadString = 0x72;
    private const byte CallMethod = 0x28;

    [Fact]
    public void AnIfOnTheVariableAloneGuardsItsBlock() =>
        Assert.Single(Guards(il => [Nop, Nop, Nop, .. il.Guard(15), .. il.Write, Return]));

    [Fact]
    public void AFlagCheckedBeforeTheGuardThatJumpsIntoItsBlockIsNoGuard() =>
        // if (force || variable == "record") write: an optimized build branches on the flag straight into the block.
        Assert.Empty(Guards(il => [LoadArgument1, ShortBranchTrue, 22, .. il.Guard(15), .. il.Write, Return]));

    [Fact]
    public void AJumpOntoTheGuardsBranchIsNoGuard() =>
        // A ternary can reach the brfalse with true already on the stack, skipping the comparison.
        Assert.Empty(Guards(il => [LoadOne, ShortBranch, 20, .. il.Guard(15), .. il.Write, Return]));

    [Fact]
    public void AnElseBlockThreadedIntoTheGuardedRangeIsNoGuard() =>
        // if (approved) { if (variable == "record") write } else write: the inner brfalse is threaded past the else block, which
        // the outer brfalse enters from outside the guard.
        Assert.Empty(Guards(il => [LoadArgument1, ShortBranchFalse, 37, .. il.Guard(30), .. il.Write, .. il.Write, Return]));

    private static IReadOnlyList<RecordGuard> Guards(Func<Instructions, byte[]> program)
    {
        MetadataBuilder metadata = new();
        metadata.AddModule(0, metadata.GetOrAddString("Il.dll"), metadata.GetOrAddGuid(Guid.Empty), default, default);
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(metadata.GetOrAddString("System.Runtime"), new Version(10, 0, 0, 0), default, default, default, default);
        Instructions instructions = new(
            Token(metadata.GetOrAddUserString("IL_SNAPSHOTS")),
            Token(metadata.GetOrAddUserString("record")),
            Token(metadata.GetOrAddUserString("case.verified.txt")),
            Method(metadata, runtime, "System", "Environment", "GetEnvironmentVariable"),
            Method(metadata, runtime, "System", "String", "op_Equality"),
            Method(metadata, runtime, "System.IO", "File", "WriteAllText"));
        BlobBuilder il = new();
        il.WriteBytes(program(instructions));
        BlobHandle body = metadata.GetOrAddBlob(il);
        BlobBuilder image = new();
        new MetadataRootBuilder(metadata).Serialize(image, 0, 0);
        using MetadataReaderProvider provider = MetadataReaderProvider.FromMetadataImage([.. image.ToArray()]);
        MetadataReader reader = provider.GetMetadataReader();
        TamperingBody walked = HostTamperingScanner.Read(reader, reader.GetBlobReader(body), default, "Il::Method", constructor: false);
        return SnapshotRecords.Guards(walked, _ => false);
    }

    private static byte[] Method(MetadataBuilder metadata, AssemblyReferenceHandle scope, string space, string type, string name)
    {
        TypeReferenceHandle reference = metadata.AddTypeReference(scope, metadata.GetOrAddString(space), metadata.GetOrAddString(type));
        return Token(metadata.AddMemberReference(reference, metadata.GetOrAddString(name), metadata.GetOrAddBlob(new byte[] { 0 })));
    }

    private static byte[] Token(EntityHandle handle) => BitConverter.GetBytes(MetadataTokens.GetToken(handle));

    private static byte[] Token(UserStringHandle handle) => BitConverter.GetBytes(MetadataTokens.GetToken(handle));

    /// <summary>The encoded instructions a program is assembled from, with their tokens.</summary>
    private sealed record Instructions(byte[] Variable, byte[] Record, byte[] Path, byte[] Read, byte[] Equality, byte[] WriteAllText)
    {
        /// <summary>A case-file write: <c>ldstr path; ldstr path; call File.WriteAllText</c>.</summary>
        public byte[] Write => [LoadString, .. Path, LoadString, .. Path, CallMethod, .. WriteAllText];

        /// <summary><c>ldstr variable; call GetEnvironmentVariable; ldstr "record"; call op_Equality; brfalse.s distance</c>.</summary>
        public byte[] Guard(byte distance) => [LoadString, .. Variable, CallMethod, .. Read, LoadString, .. Record, CallMethod, .. Equality, ShortBranchFalse, distance];
    }
}
