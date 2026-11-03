using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Lockflight.Cli.Ci;

/// <summary>
/// A value-flow pass over one method's IL: for every call, the values of its arguments (<see cref="FlowValue"/>), so the snapshot
/// record mode (<see cref="SnapshotRecords"/>) can tell a path built from literals from one that comes from a parameter, a field, a
/// call, or a <c>[CallerFilePath]</c>, and a literal variable name from one built at run time. Locals are tracked without regard
/// to order (a local that ever holds another value is never from literals), branch targets merge the stacks that reach them, and
/// anything the pass cannot follow (an unknown opcode, stacks that disagree, <c>calli</c>, varargs) makes the whole method
/// <see cref="Understood"/> false, which allows nothing.
/// </summary>
internal sealed class IlValueFlow
{
    private const int MaxPasses = 8;
    private const byte TwoBytePrefix = 0xFE;

    private static readonly string[] LiteralFunctions = ["System.IO.Path::Combine", "System.IO.Path::Join", "System.String::Concat"];

    private readonly Dictionary<int, IReadOnlyList<FlowValue>> arguments = [];

    private IlValueFlow(bool understood) => Understood = understood;

    /// <summary>Whether the pass followed the whole method; when false, <see cref="ArgumentsAt"/> knows nothing.</summary>
    public bool Understood { get; }

    public static IlValueFlow None { get; } = new(false);

    /// <summary>The arguments of the call at <paramref name="offset"/>, in order (an instance call's target first); null when not known.</summary>
    public IReadOnlyList<FlowValue>? ArgumentsAt(int offset) => Understood && arguments.TryGetValue(offset, out IReadOnlyList<FlowValue>? values) ? values : null;

    /// <summary>
    /// Follows the method body. <paramref name="callText"/> names a call token as the scanner does (<c>Type::Member</c>, with
    /// <c>@other</c> for a type outside the runtime's assemblies).
    /// </summary>
    public static IlValueFlow Analyze(MetadataReader reader, MethodBodyBlock block, Func<EntityHandle, string> callText)
    {
        Dictionary<int, bool> localsFromLiterals = [];
        for (int pass = 0; pass < MaxPasses; pass++)
        {
            IlValueFlow flow = new(true);
            Dictionary<int, bool> stored = [];
            if (!flow.Walk(reader, block, callText, localsFromLiterals, stored))
            {
                return None;
            }

            if (stored.All(entry => localsFromLiterals.TryGetValue(entry.Key, out bool known) && known == entry.Value))
            {
                return flow;
            }

            foreach ((int slot, bool fromLiterals) in stored)
            {
                localsFromLiterals[slot] = fromLiterals && (!localsFromLiterals.TryGetValue(slot, out bool known) || known);
            }
        }

        return None;
    }

    private bool Walk(MetadataReader reader, MethodBodyBlock block, Func<EntityHandle, string> callText, Dictionary<int, bool> locals, Dictionary<int, bool> stored)
    {
        Dictionary<int, List<FlowValue>> pending = [];
        foreach (ExceptionRegion region in block.ExceptionRegions)
        {
            bool catches = region.Kind is ExceptionRegionKind.Catch or ExceptionRegionKind.Filter;
            if (!Merge(pending, -1, region.HandlerOffset, catches ? [FlowValue.Unknown] : []))
            {
                return false;
            }

            if (region.Kind == ExceptionRegionKind.Filter && !Merge(pending, -1, region.FilterOffset, [FlowValue.Unknown]))
            {
                return false;
            }
        }

        BlobReader il = block.GetILReader();
        List<FlowValue> stack = [];
        bool reachable = true;
        while (il.RemainingBytes > 0)
        {
            int offset = il.Offset;
            if (pending.Remove(offset, out List<FlowValue>? incoming))
            {
                if (reachable && !SameDepth(stack, incoming))
                {
                    return false;
                }

                stack = reachable ? [.. stack.Zip(incoming, Join)] : incoming;
            }
            else if (!reachable)
            {
                stack = [];
            }

            reachable = true;
            byte code = il.ReadByte();
            if (code == TwoBytePrefix)
            {
                if (!TwoByte(il.ReadByte(), ref il, stack, locals, stored))
                {
                    return false;
                }

                continue;
            }

            switch (code)
            {
                case >= 0x02 and <= 0x05:
                    stack.Add(FlowValue.Unknown);
                    break;
                case 0x14 or (>= 0x15 and <= 0x1E):
                    stack.Add(FlowValue.Constant);
                    break;
                case 0x1F:
                    il.ReadByte();
                    stack.Add(FlowValue.Constant);
                    break;
                case 0x20 or 0x22:
                    il.ReadInt32();
                    stack.Add(FlowValue.Constant);
                    break;
                case 0x21 or 0x23:
                    il.ReadInt64();
                    stack.Add(FlowValue.Constant);
                    break;
                case >= 0x06 and <= 0x09:
                    stack.Add(Local(locals, code - 0x06));
                    break;
                case 0x11:
                    stack.Add(Local(locals, il.ReadByte()));
                    break;
                case >= 0x0A and <= 0x0D:
                    if (!Store(stack, stored, code - 0x0A))
                    {
                        return false;
                    }

                    break;
                case 0x13:
                    if (!Store(stack, stored, il.ReadByte()))
                    {
                        return false;
                    }

                    break;
                case 0x0E or 0x0F or 0x12:
                    il.ReadByte();
                    stack.Add(FlowValue.Unknown);
                    break;
                case 0x10:
                    il.ReadByte();
                    if (!Pop(stack, 1))
                    {
                        return false;
                    }

                    break;
                case 0x00 or 0x01:
                    break;
                case 0x25:
                    if (stack.Count == 0)
                    {
                        return false;
                    }

                    stack.Add(stack[^1]);
                    break;
                case 0x26:
                    if (!Pop(stack, 1))
                    {
                        return false;
                    }

                    break;
                case 0x72:
                    stack.Add(new FlowValue(true, reader.GetUserString(MetadataTokens.UserStringHandle(il.ReadInt32() & 0x00FFFFFF))));
                    break;
                case 0x28 or 0x6F or 0x73:
                    if (!Call(reader, MetadataTokens.EntityHandle(il.ReadInt32()), offset, code == 0x73, stack, callText))
                    {
                        return false;
                    }

                    break;
                case 0x2A or 0x7A:
                    stack = [];
                    reachable = false;
                    break;
                case 0x2B or 0xDE:
                    int shortTarget = il.ReadSByte() + il.Offset;
                    if (code == 0x2B && !Merge(pending, offset, shortTarget, stack))
                    {
                        return false;
                    }

                    if (code == 0xDE && !Merge(pending, offset, shortTarget, []))
                    {
                        return false;
                    }

                    stack = [];
                    reachable = false;
                    break;
                case 0x38 or 0xDD:
                    int longTarget = il.ReadInt32() + il.Offset;
                    if (!Merge(pending, offset, longTarget, code == 0x38 ? stack : []))
                    {
                        return false;
                    }

                    stack = [];
                    reachable = false;
                    break;
                case 0x2C or 0x2D:
                    if (!Branch(pending, stack, 1, offset, il.ReadSByte() + il.Offset))
                    {
                        return false;
                    }

                    break;
                case >= 0x2E and <= 0x37:
                    if (!Branch(pending, stack, 2, offset, il.ReadSByte() + il.Offset))
                    {
                        return false;
                    }

                    break;
                case 0x39 or 0x3A:
                    if (!Branch(pending, stack, 1, offset, il.ReadInt32() + il.Offset))
                    {
                        return false;
                    }

                    break;
                case >= 0x3B and <= 0x44:
                    if (!Branch(pending, stack, 2, offset, il.ReadInt32() + il.Offset))
                    {
                        return false;
                    }

                    break;
                case 0x45:
                    int count = il.ReadInt32();
                    int tableEnd = il.Offset + (count * sizeof(int));
                    if (!Pop(stack, 1))
                    {
                        return false;
                    }

                    for (int entry = 0; entry < count; entry++)
                    {
                        if (!Merge(pending, offset, tableEnd + il.ReadInt32(), stack))
                        {
                            return false;
                        }
                    }

                    break;
                default:
                    if (Effect(code) is not (int pops, int pushes, int operand) || !Pop(stack, pops))
                    {
                        return false;
                    }

                    il.Offset += operand;
                    for (int push = 0; push < pushes; push++)
                    {
                        stack.Add(FlowValue.Unknown);
                    }

                    break;
            }
        }

        return pending.Count == 0;
    }

    /// <summary>The two-byte instructions: comparisons, <c>ldftn</c>, argument and local access, and prefixes.</summary>
    private static bool TwoByte(byte code, ref BlobReader il, List<FlowValue> stack, Dictionary<int, bool> locals, Dictionary<int, bool> stored)
    {
        switch (code)
        {
            case 0x00 or 0x06:
                il.Offset += code == 0x06 ? 4 : 0;
                stack.Add(FlowValue.Unknown);
                return true;
            case >= 0x01 and <= 0x05:
                if (!Pop(stack, 2))
                {
                    return false;
                }

                stack.Add(FlowValue.Unknown);
                return true;
            case 0x07:
                il.Offset += 4;
                if (!Pop(stack, 1))
                {
                    return false;
                }

                stack.Add(FlowValue.Unknown);
                return true;
            case 0x09 or 0x0A or 0x0D:
                il.ReadUInt16();
                stack.Add(FlowValue.Unknown);
                return true;
            case 0x0C:
                stack.Add(Local(locals, il.ReadUInt16()));
                return true;
            case 0x0B:
                il.ReadUInt16();
                return Pop(stack, 1);
            case 0x0E:
                return Store(stack, stored, il.ReadUInt16());
            case 0x0F or 0x1D:
                if (!Pop(stack, 1))
                {
                    return false;
                }

                stack.Add(FlowValue.Unknown);
                return true;
            case 0x11 or 0x15:
                il.Offset += code == 0x15 ? 4 : 0;
                return Pop(stack, 1);
            case 0x12 or 0x19:
                il.ReadByte();
                return true;
            case 0x13 or 0x14 or 0x1E:
                return true;
            case 0x16:
                il.ReadInt32();
                return true;
            case 0x17 or 0x18:
                return Pop(stack, 3);
            case 0x1A:
                stack.Clear();
                return true;
            case 0x1C:
                il.ReadInt32();
                stack.Add(FlowValue.Constant);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Pops a call's arguments, records them, and pushes its result: from literals only for a literal function of literals.</summary>
    private bool Call(MetadataReader reader, EntityHandle token, int offset, bool construct, List<FlowValue> stack, Func<EntityHandle, string> callText)
    {
        if (Signature(reader, token) is not (int parameters, bool instance, bool returns))
        {
            return false;
        }

        int count = parameters + (instance && !construct ? 1 : 0);
        if (stack.Count < count)
        {
            return false;
        }

        List<FlowValue> values = stack[^count..];
        stack.RemoveRange(stack.Count - count, count);
        arguments[offset] = values;
        if (construct || returns)
        {
            stack.Add(!construct && LiteralFunctions.Contains(callText(token), StringComparer.Ordinal) && values.All(value => value.FromLiterals)
                ? new FlowValue(true, null)
                : FlowValue.Unknown);
        }

        return true;
    }

    /// <summary>The parameter count, whether the method takes <c>this</c>, and whether it returns a value; null for varargs or an unknown token.</summary>
    private static (int Parameters, bool Instance, bool Returns)? Signature(MetadataReader reader, EntityHandle token)
    {
        BlobHandle signature;
        switch (token.Kind)
        {
            case HandleKind.MethodDefinition:
                signature = reader.GetMethodDefinition((MethodDefinitionHandle)token).Signature;
                break;
            case HandleKind.MemberReference:
                signature = reader.GetMemberReference((MemberReferenceHandle)token).Signature;
                break;
            case HandleKind.MethodSpecification:
                return Signature(reader, reader.GetMethodSpecification((MethodSpecificationHandle)token).Method);
            default:
                return null;
        }

        BlobReader blob = reader.GetBlobReader(signature);
        SignatureHeader header = blob.ReadSignatureHeader();
        if (header.Kind != SignatureKind.Method || header.CallingConvention == SignatureCallingConvention.VarArgs)
        {
            return null;
        }

        if (header.IsGeneric)
        {
            blob.ReadCompressedInteger();
        }

        int parameters = blob.ReadCompressedInteger();
        SignatureTypeCode returned = blob.ReadSignatureTypeCode();
        while (returned is SignatureTypeCode.RequiredModifier or SignatureTypeCode.OptionalModifier)
        {
            blob.ReadTypeHandle();
            returned = blob.ReadSignatureTypeCode();
        }

        return (parameters, header.IsInstance, returned != SignatureTypeCode.Void);
    }

    /// <summary>The stack effect and operand size of the one-byte instructions the walk does not handle itself; null for one it does not know.</summary>
    private static (int Pops, int Pushes, int Operand)? Effect(byte code) => code switch
    {
        >= 0x46 and <= 0x50 => (1, 1, 0),
        >= 0x51 and <= 0x57 => (2, 0, 0),
        >= 0x58 and <= 0x64 => (2, 1, 0),
        0x65 or 0x66 => (1, 1, 0),
        >= 0x67 and <= 0x6E => (1, 1, 0),
        0x70 => (2, 0, 4),
        0x71 => (1, 1, 4),
        0x74 or 0x75 => (1, 1, 4),
        0x76 => (1, 1, 0),
        0x79 => (1, 1, 4),
        0x7B or 0x7C => (1, 1, 4),
        0x7D => (2, 0, 4),
        0x7E or 0x7F => (0, 1, 4),
        0x80 => (1, 0, 4),
        0x81 => (2, 0, 4),
        >= 0x82 and <= 0x8B => (1, 1, 0),
        0x8C or 0x8D => (1, 1, 4),
        0x8E => (1, 1, 0),
        0x8F => (2, 1, 4),
        >= 0x90 and <= 0x9A => (2, 1, 0),
        >= 0x9B and <= 0xA2 => (3, 0, 0),
        0xA3 => (2, 1, 4),
        0xA4 => (3, 0, 4),
        0xA5 => (1, 1, 4),
        >= 0xB3 and <= 0xBA => (1, 1, 0),
        0xC2 => (1, 1, 4),
        0xC3 => (1, 1, 0),
        0xC6 => (1, 1, 4),
        0xD0 => (0, 1, 4),
        >= 0xD1 and <= 0xD5 => (1, 1, 0),
        >= 0xD6 and <= 0xDB => (2, 1, 0),
        0xDC => (0, 0, 0),
        0xDF => (2, 0, 0),
        0xE0 => (1, 1, 0),
        _ => null,
    };

    /// <summary>A local's value: from literals unless an earlier pass saw it hold anything else.</summary>
    private static FlowValue Local(Dictionary<int, bool> locals, int slot) =>
        locals.TryGetValue(slot, out bool fromLiterals) && !fromLiterals ? FlowValue.Unknown : FlowValue.Constant;

    private static bool Store(List<FlowValue> stack, Dictionary<int, bool> stored, int slot)
    {
        if (stack.Count == 0)
        {
            return false;
        }

        stored[slot] = stack[^1].FromLiterals && (!stored.TryGetValue(slot, out bool earlier) || earlier);
        stack.RemoveAt(stack.Count - 1);
        return true;
    }

    private static bool Branch(Dictionary<int, List<FlowValue>> pending, List<FlowValue> stack, int pops, int from, int target) =>
        Pop(stack, pops) && Merge(pending, from, target, stack);

    private static bool Pop(List<FlowValue> stack, int count)
    {
        if (stack.Count < count)
        {
            return false;
        }

        stack.RemoveRange(stack.Count - count, count);
        return true;
    }

    /// <summary>
    /// Records the stack that reaches <paramref name="target"/> from <paramref name="from"/>; false when it disagrees in depth with one
    /// recorded before. A backward jump (a loop) must carry an empty stack, as the C# compiler's do: the walk has passed its target.
    /// </summary>
    private static bool Merge(Dictionary<int, List<FlowValue>> pending, int from, int target, List<FlowValue> stack)
    {
        if (target <= from)
        {
            return stack.Count == 0;
        }

        if (!pending.TryGetValue(target, out List<FlowValue>? earlier))
        {
            pending[target] = [.. stack];
            return true;
        }

        if (!SameDepth(earlier, stack))
        {
            return false;
        }

        pending[target] = [.. earlier.Zip(stack, Join)];
        return true;
    }

    private static bool SameDepth(List<FlowValue> first, List<FlowValue> second) => first.Count == second.Count;

    private static FlowValue Join(FlowValue first, FlowValue second) =>
        first.FromLiterals && second.FromLiterals ? new FlowValue(true, first.Literal == second.Literal ? first.Literal : null) : FlowValue.Unknown;
}
