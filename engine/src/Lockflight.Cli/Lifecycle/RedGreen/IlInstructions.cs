using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Lockflight.Cli.Lifecycle.RedGreen;

/// <summary>
/// Walks a method body's IL (ECMA-335 III) far enough to collect the metadata tokens of the methods it calls, constructs or takes a
/// delegate to (<c>call</c>, <c>callvirt</c>, <c>newobj</c>, <c>jmp</c>, <c>ldftn</c>, <c>ldvirtftn</c>) and to see whether it throws
/// (<c>throw</c>, <c>rethrow</c>); every other operand is skipped by its size.
/// </summary>
internal static class IlInstructions
{
    private const byte TwoBytePrefixOpCode = 0xFE;
    private const byte SwitchOpCode = 0x45;
    private const byte ThrowOpCode = 0x7A;
    private const byte RethrowOpCode = 0x1A;

    private static readonly HashSet<byte> MethodOpCodes = [0x27, 0x28, 0x6F, 0x73];
    private static readonly HashSet<byte> TwoByteMethodOpCodes = [0x06, 0x07];

    public static IlBody Read(BlobReader il)
    {
        List<EntityHandle> tokens = [];
        bool throws = false;
        while (il.RemainingBytes > 0)
        {
            byte code = il.ReadByte();
            if (code == TwoBytePrefixOpCode)
            {
                byte second = il.ReadByte();
                throws |= second == RethrowOpCode;
                if (TwoByteMethodOpCodes.Contains(second))
                {
                    tokens.Add(MetadataTokens.EntityHandle(il.ReadInt32()));
                }
                else
                {
                    il.Offset += TwoByteOperandSize(second);
                }
            }
            else if (MethodOpCodes.Contains(code))
            {
                tokens.Add(MetadataTokens.EntityHandle(il.ReadInt32()));
            }
            else if (code == SwitchOpCode)
            {
                il.Offset += il.ReadInt32() * sizeof(int);
            }
            else
            {
                throws |= code == ThrowOpCode;
                il.Offset += OneByteOperandSize(code);
            }
        }

        return new IlBody(tokens, throws);
    }

    private static int OneByteOperandSize(byte code) => code switch
    {
        >= 0x0E and <= 0x13 => 1,    // ldarg.s … stloc.s
        0x1F => 1,                   // ldc.i4.s
        0x20 or 0x22 => 4,           // ldc.i4, ldc.r4
        0x21 or 0x23 => 8,           // ldc.i8, ldc.r8
        0x29 => 4,                   // calli
        >= 0x2B and <= 0x37 => 1,    // short branches
        >= 0x38 and <= 0x44 => 4,    // long branches
        >= 0x70 and <= 0x75 => 4,    // cpobj, ldobj, ldstr, castclass, isinst
        0x79 => 4,                   // unbox
        >= 0x7B and <= 0x81 => 4,    // field access, stobj
        0x8C or 0x8D or 0x8F => 4,   // box, newarr, ldelema
        >= 0xA3 and <= 0xA5 => 4,    // ldelem, stelem, unbox.any
        0xC2 or 0xC6 or 0xD0 => 4,   // refanyval, mkrefany, ldtoken
        0xDD => 4,                   // leave
        0xDE => 1,                   // leave.s
        _ => 0,
    };

    private static int TwoByteOperandSize(byte code) => code switch
    {
        >= 0x09 and <= 0x0E => 2,    // ldarg … stloc
        0x12 or 0x19 => 1,           // unaligned., no.
        0x15 or 0x16 or 0x1C => 4,   // initobj, constrained., sizeof
        _ => 0,
    };
}
