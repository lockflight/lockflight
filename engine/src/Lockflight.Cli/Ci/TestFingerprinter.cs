using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The per-method fingerprint of a test, read from a built test assembly without loading it (AOT-safe): a SHA-256 over the IL of
/// the test method and of every method of the test assembly it reaches (helpers in any class, base classes, lambdas, local functions,
/// async and iterator state machines), its class's constructors and the module initializer, with every metadata token written as the
/// name it stands for, and the test's attributes (<c>InlineData</c> values, traits, <c>Skip</c>). Compiler-generated names are
/// replaced by their order in the walk, because their ordinals shift when a test is added above; members of assemblies outside the
/// repository carry their version, so a swapped or downgraded assertion library changes the fingerprint while a per-commit version
/// of the repository's own assemblies does not. Anything the walk cannot read, or a walk past its bound, gives no fingerprint, which
/// counts as changed.
/// </summary>
internal sealed class TestFingerprinter(string assemblyPath, IReadOnlySet<string> repositoryAssemblies) : IDisposable
{
    private const int MaxMethods = 5000;
    private const byte TwoBytePrefix = 0xFE;
    private const byte SwitchOpCode = 0x45;
    private const byte StringOpCode = 0x72;
    private const byte CallVirtOpCode = 0x6F;

    private static readonly string[] CompilerAttributes = ["]System.Runtime.CompilerServices.", "]System.Diagnostics."];
    private static readonly HashSet<byte> MethodOps = [0x27, 0x28, 0x6F, 0x73];
    private static readonly HashSet<byte> TokenOps = [0x70, 0x71, 0x74, 0x75, 0x79, 0x7B, 0x7C, 0x7D, 0x7E, 0x7F, 0x80, 0x81, 0x8C, 0x8D, 0x8F, 0xA3, 0xA4, 0xA5, 0xC2, 0xC6, 0xD0];
    private static readonly HashSet<byte> TwoByteMethodOps = [0x06, 0x07];
    private static readonly HashSet<byte> TwoByteTokenOps = [0x15, 0x16, 0x1C];

    private readonly PEReader pe = new(File.OpenRead(assemblyPath));

    /// <summary>The fingerprint of <paramref name="className"/>.<paramref name="method"/>; null when it cannot be computed (counts as changed).</summary>
    public string? Fingerprint(string className, string method) => CanonicalText(className, method) is string text
        ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
        : null;

    /// <summary>The text the fingerprint hashes, for a person comparing two fingerprints; null when it cannot be computed.</summary>
    public string? CanonicalText(string className, string method)
    {
        try
        {
            return new Walk(pe, repositoryAssemblies).Run(className, method);
        }
        catch (Exception exception) when (exception is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    public void Dispose() => pe.Dispose();

    /// <summary>One fingerprint's walk: the canonical text it builds and the generated names it has seen, in order.</summary>
    private sealed class Walk(PEReader pe, IReadOnlySet<string> repositoryAssemblies)
    {
        private readonly MetadataReader reader = pe.GetMetadataReader();
        private readonly StringBuilder text = new();
        private readonly Dictionary<string, string> generated = new(StringComparer.Ordinal);
        private readonly Queue<MethodDefinitionHandle> pending = new();
        private readonly HashSet<MethodDefinitionHandle> visited = [];
        private SignatureNames? names;
        private BlobReader signatureBlob;

        private SignatureNames Names => names ??= new SignatureNames(DefinitionName, ReferenceName, reader);

        public string? Run(string className, string method)
        {
            if (FindType(className) is not TypeDefinitionHandle type)
            {
                return null;
            }

            MethodDefinitionHandle[] roots = [.. MethodsNamed(type, method)];
            if (roots.Length != 1)
            {
                return null;
            }

            MethodDefinition test = reader.GetMethodDefinition(roots[0]);
            foreach (CustomAttributeHandle handle in test.GetCustomAttributes())
            {
                CustomAttribute attribute = reader.GetCustomAttribute(handle);
                string constructor = Token(attribute.Constructor);

                // The compiler's own attributes name generated types with their ordinals (AsyncStateMachine(typeof(<T>d__5))).
                if (!CompilerAttributes.Any(prefix => constructor.Contains(prefix, StringComparison.Ordinal)))
                {
                    text.Append("attr ").Append(constructor).Append(' ').Append(AttributeValue(attribute)).Append('\n');
                }
            }

            pending.Enqueue(roots[0]);
            EnqueueFrameworkCode(type, test);
            for (TypeDefinitionHandle? current = type; current is TypeDefinitionHandle currentType; current = BaseInAssembly(currentType))
            {
                foreach (MethodDefinitionHandle constructor in MethodsNamed(currentType, ".ctor").Concat(MethodsNamed(currentType, ".cctor")))
                {
                    pending.Enqueue(constructor);
                }
            }

            foreach (TypeDefinitionHandle moduleType in reader.TypeDefinitions.Where(handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == "<Module>"))
            {
                foreach (MethodDefinitionHandle initializer in MethodsNamed(moduleType, ".cctor"))
                {
                    pending.Enqueue(initializer);
                }
            }

            while (pending.Count > 0)
            {
                if (visited.Count >= MaxMethods)
                {
                    return null;
                }

                MethodDefinitionHandle handle = pending.Dequeue();
                if (visited.Add(handle) && !Method(handle))
                {
                    return null;
                }
            }

            return text.ToString();
        }

        /// <summary>
        /// An attribute's arguments without the caller information the compiler fills in: xUnit's <c>[Fact]</c> and <c>[Theory]</c>
        /// take the source file and line, which move whenever a test is added above. A constructor of this assembly drops the
        /// parameters marked <c>[CallerFilePath]</c>, <c>[CallerLineNumber]</c> or <c>[CallerMemberName]</c>; one of an xUnit assembly
        /// drops a trailing (string, int) pair, which is how xUnit declares them.
        /// </summary>
        private string AttributeValue(CustomAttribute attribute)
        {
            CustomAttributeValue<string> value = attribute.DecodeValue(new AttributeTypes(Names));
            HashSet<int> callerInfo = CallerInfoPositions(attribute.Constructor, value.FixedArguments);
            IEnumerable<string> fixedArguments = value.FixedArguments.Where((_, index) => !callerInfo.Contains(index)).Select(argument => $"{argument.Type}={Argument(argument.Value)}");
            IEnumerable<string> namedArguments = value.NamedArguments.Select(argument => $"{argument.Name}:{argument.Type}={Argument(argument.Value)}");
            return $"({string.Join(',', fixedArguments)}) {{{string.Join(',', namedArguments)}}}";
        }

        private HashSet<int> CallerInfoPositions(EntityHandle constructor, IReadOnlyList<CustomAttributeTypedArgument<string>> arguments)
        {
            HashSet<int> positions = [];
            if (constructor.Kind == HandleKind.MethodDefinition)
            {
                foreach (ParameterHandle handle in reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetParameters())
                {
                    Parameter parameter = reader.GetParameter(handle);
                    if (parameter.SequenceNumber > 0 && parameter.GetCustomAttributes().Any(attributeHandle => CallerAttribute(reader.GetCustomAttribute(attributeHandle).Constructor)))
                    {
                        positions.Add(parameter.SequenceNumber - 1);
                    }
                }
            }
            else if (Token(constructor).StartsWith("[xunit", StringComparison.OrdinalIgnoreCase) && arguments.Count >= 2
                && arguments[^2].Type == "String" && arguments[^1].Type == "Int32")
            {
                positions.Add(arguments.Count - 2);
                positions.Add(arguments.Count - 1);
            }

            return positions;
        }

        private bool CallerAttribute(EntityHandle constructor) =>
            Token(constructor) is string name && (name.Contains("CallerFilePathAttribute", StringComparison.Ordinal)
                || name.Contains("CallerLineNumberAttribute", StringComparison.Ordinal) || name.Contains("CallerMemberNameAttribute", StringComparison.Ordinal));

        private static string Argument(object? value) => value switch
        {
            null => "null",
            ImmutableArray<CustomAttributeTypedArgument<string>> array => $"[{string.Join(',', array.Select(item => Argument(item.Value)))}]",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => $"\"{value}\"",
        };

        /// <summary>Appends one method's name, signature and IL; false when its body cannot be read.</summary>
        private bool Method(MethodDefinitionHandle handle)
        {
            MethodDefinition definition = reader.GetMethodDefinition(handle);
            text.Append("method ").Append(Token(handle)).Append('\n');
            foreach (TypeDefinitionHandle nested in reader.GetTypeDefinition(definition.GetDeclaringType()).GetNestedTypes()
                .Where(nested => reader.GetString(reader.GetTypeDefinition(nested).Name).StartsWith($"<{reader.GetString(definition.Name)}>d", StringComparison.Ordinal)))
            {
                foreach (MethodDefinitionHandle stateMachine in reader.GetTypeDefinition(nested).GetMethods())
                {
                    pending.Enqueue(stateMachine);
                }
            }

            if (definition.RelativeVirtualAddress == 0)
            {
                return true;
            }

            BlobReader il = pe.GetMethodBody(definition.RelativeVirtualAddress).GetILReader();
            while (il.RemainingBytes > 0)
            {
                byte code = il.ReadByte();
                text.Append(code.ToString("x2", CultureInfo.InvariantCulture));
                if (code == TwoBytePrefix)
                {
                    byte second = il.ReadByte();
                    text.Append(second.ToString("x2", CultureInfo.InvariantCulture));
                    if (TwoByteMethodOps.Contains(second) || TwoByteTokenOps.Contains(second))
                    {
                        Operand(MetadataTokens.EntityHandle(il.ReadInt32()));
                    }
                    else
                    {
                        Raw(ref il, TwoByteOperandSize(second));
                    }
                }
                else if (MethodOps.Contains(code) || TokenOps.Contains(code))
                {
                    EntityHandle token = MetadataTokens.EntityHandle(il.ReadInt32());
                    Operand(token);
                    if (code == CallVirtOpCode)
                    {
                        EnqueueOverrides(token);
                    }
                }
                else if (code == StringOpCode)
                {
                    text.Append(" \"").Append(reader.GetUserString(MetadataTokens.UserStringHandle(il.ReadInt32() & 0x00FFFFFF))).Append('"');
                }
                else if (code == SwitchOpCode)
                {
                    int count = il.ReadInt32();
                    text.Append(' ').Append(count);
                    Raw(ref il, count * sizeof(int));
                }
                else
                {
                    Raw(ref il, OneByteOperandSize(code));
                }

                text.Append(';');
            }

            text.Append('\n');
            return true;
        }

        private void Operand(EntityHandle token)
        {
            text.Append(' ').Append(Token(token));
            foreach (MethodDefinitionHandle own in OwnMethods(token))
            {
                pending.Enqueue(own);
            }

            // A static field's value comes from its type's static constructor.
            if (token.Kind == HandleKind.FieldDefinition)
            {
                EnqueueAll(reader.GetFieldDefinition((FieldDefinitionHandle)token).GetDeclaringType(), ".cctor");
            }
        }

        /// <summary>
        /// A virtual or interface call reaches whichever override runs: every virtual method of this assembly with the same name. A
        /// generic method call is followed through to the method it instantiates. For a member of the base class library (or
        /// another <c>Microsoft.</c> or <c>System.</c> assembly) only the overrides in types of this assembly that derive from or
        /// implement the called type count, so a fake <c>Stream</c> or <c>HttpMessageHandler</c> is reached while every
        /// <c>ToString</c> and <c>Equals</c> is not; members of <c>System.Object</c> are never followed.
        /// </summary>
        private void EnqueueOverrides(EntityHandle token)
        {
            if (token.Kind == HandleKind.MethodSpecification)
            {
                EnqueueOverrides(reader.GetMethodSpecification((MethodSpecificationHandle)token).Method);
                return;
            }

            string? name = null;
            string? framework = null;
            if (token.Kind == HandleKind.MethodDefinition)
            {
                MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)token);
                name = (method.Attributes & MethodAttributes.Virtual) != 0 ? reader.GetString(method.Name) : null;
            }
            else if (token.Kind == HandleKind.MemberReference)
            {
                MemberReference member = reader.GetMemberReference((MemberReferenceHandle)token);
                string parent = member.Parent.Kind switch
                {
                    HandleKind.TypeReference => ReferenceName((TypeReferenceHandle)member.Parent),
                    HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle)member.Parent).DecodeSignature(Names, null),
                    _ => string.Empty,
                };
                name = reader.GetString(member.Name);
                if (IsFramework(parent))
                {
                    framework = WithoutArguments(parent);
                    if (framework.EndsWith("]System.Object", StringComparison.Ordinal))
                    {
                        return;
                    }
                }
            }

            if (name is null)
            {
                return;
            }

            foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
            {
                MethodDefinition candidate = reader.GetMethodDefinition(handle);
                if ((candidate.Attributes & MethodAttributes.Virtual) != 0 && reader.GetString(candidate.Name) == name
                    && (framework is null || DerivesFrom(candidate.GetDeclaringType(), framework)))
                {
                    pending.Enqueue(handle);
                }
            }
        }

        private static bool IsFramework(string typeName) =>
            typeName.Contains("]System.", StringComparison.Ordinal) || typeName.Contains("]Microsoft.", StringComparison.Ordinal);

        private static string WithoutArguments(string typeName) => typeName.IndexOf('<', StringComparison.Ordinal) is int open and >= 0 ? typeName[..open] : typeName;

        /// <summary>Whether a type of this assembly derives from or implements <paramref name="framework"/> (its name without type arguments).</summary>
        private bool DerivesFrom(TypeDefinitionHandle type, string framework)
        {
            for (TypeDefinitionHandle? current = type; current is TypeDefinitionHandle currentType; current = BaseInAssembly(currentType))
            {
                TypeDefinition definition = reader.GetTypeDefinition(currentType);
                if (!definition.BaseType.IsNil && definition.BaseType.Kind != HandleKind.TypeDefinition && WithoutArguments(Token(definition.BaseType)) == framework)
                {
                    return true;
                }

                if (definition.GetInterfaceImplementations().Any(implementation => WithoutArguments(Token(reader.GetInterfaceImplementation(implementation).Interface)) == framework))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// What the test framework calls around the test rather than the test itself: the members a data attribute names
        /// (<c>MemberData</c>, <c>ClassData</c>, NUnit's <c>TestCaseSource</c>) and the fixtures the class takes
        /// (<c>IClassFixture&lt;T&gt;</c> and the like), all of their code.
        /// </summary>
        private void EnqueueFrameworkCode(TypeDefinitionHandle type, MethodDefinition test)
        {
            foreach (CustomAttributeHandle handle in test.GetCustomAttributes())
            {
                CustomAttribute attribute = reader.GetCustomAttribute(handle);
                string constructor = Token(attribute.Constructor);
                if (!constructor.Contains("DataAttribute", StringComparison.Ordinal) && !constructor.Contains("SourceAttribute", StringComparison.Ordinal))
                {
                    continue;
                }

                CustomAttributeValue<string> value = attribute.DecodeValue(new AttributeTypes(Names));
                foreach (object? argument in value.FixedArguments.Select(argument => argument.Value).Concat(value.NamedArguments.Select(argument => argument.Value)))
                {
                    if (argument is string named)
                    {
                        foreach (TypeDefinitionHandle candidate in reader.TypeDefinitions)
                        {
                            EnqueueAll(candidate, named);
                            EnqueueAll(candidate, $"get_{named}");
                            if (PlainName(candidate) == named || PlainName(candidate).EndsWith("." + named, StringComparison.Ordinal))
                            {
                                EnqueueAll(candidate, name: null);
                            }
                        }
                    }
                }

                EnqueueAll(type, ".cctor");
            }

            for (TypeDefinitionHandle? current = type; current is TypeDefinitionHandle currentType; current = BaseInAssembly(currentType))
            {
                foreach (InterfaceImplementationHandle implementation in reader.GetTypeDefinition(currentType).GetInterfaceImplementations())
                {
                    EntityHandle contract = reader.GetInterfaceImplementation(implementation).Interface;
                    if (contract.Kind != HandleKind.TypeSpecification)
                    {
                        continue;
                    }

                    BlobReader signature = reader.GetBlobReader(reader.GetTypeSpecification((TypeSpecificationHandle)contract).Signature);
                    if (signature.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
                    {
                        continue;
                    }

                    signature.ReadSignatureTypeCode();
                    signature.ReadTypeHandle();
                    int count = signature.ReadCompressedInteger();
                    for (int index = 0; index < count; index++)
                    {
                        if (signature.ReadSignatureTypeCode() is SignatureTypeCode.TypeHandle && signature.ReadTypeHandle() is { Kind: HandleKind.TypeDefinition } fixture)
                        {
                            EnqueueAll((TypeDefinitionHandle)fixture, name: null);
                        }
                    }
                }
            }
        }

        /// <summary>Every method of <paramref name="type"/> named <paramref name="name"/> (all of them when null).</summary>
        private void EnqueueAll(TypeDefinitionHandle type, string? name)
        {
            foreach (MethodDefinitionHandle handle in reader.GetTypeDefinition(type).GetMethods())
            {
                if (name is null || reader.GetString(reader.GetMethodDefinition(handle).Name) == name)
                {
                    pending.Enqueue(handle);
                }
            }
        }

        private void Raw(ref BlobReader il, int size)
        {
            if (size > 0)
            {
                text.Append(' ').Append(Convert.ToHexString(il.ReadBytes(size)));
            }
        }

        /// <summary>What a token stands for, by name.</summary>
        private string Token(EntityHandle token)
        {
            switch (token.Kind)
            {
                case HandleKind.MethodDefinition:
                    MethodDefinition method = reader.GetMethodDefinition((MethodDefinitionHandle)token);
                    return $"{DefinitionName(method.GetDeclaringType())}::{Canonical(reader.GetString(method.Name))}{MethodSignature(method.Signature)}";
                case HandleKind.MemberReference:
                    MemberReference member = reader.GetMemberReference((MemberReferenceHandle)token);
                    string parent = member.Parent.Kind switch
                    {
                        HandleKind.TypeReference => ReferenceName((TypeReferenceHandle)member.Parent),
                        HandleKind.TypeDefinition => DefinitionName((TypeDefinitionHandle)member.Parent),
                        HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle)member.Parent).DecodeSignature(Names, null),
                        HandleKind.MethodDefinition => Token(member.Parent),
                        _ => "?",
                    };
                    string signature = member.GetKind() == MemberReferenceKind.Method ? MethodSignature(member.Signature) : $":{member.DecodeFieldSignature(Names, null)}";
                    return $"{parent}::{Canonical(reader.GetString(member.Name))}{signature}";
                case HandleKind.MethodSpecification:
                    MethodSpecification specification = reader.GetMethodSpecification((MethodSpecificationHandle)token);
                    return $"{Token(specification.Method)}<{string.Join(',', specification.DecodeSignature(Names, null))}>";
                case HandleKind.FieldDefinition:
                    FieldDefinition field = reader.GetFieldDefinition((FieldDefinitionHandle)token);
                    return $"{DefinitionName(field.GetDeclaringType())}::{Canonical(reader.GetString(field.Name))}:{field.DecodeSignature(Names, null)}";
                case HandleKind.TypeDefinition:
                    return DefinitionName((TypeDefinitionHandle)token);
                case HandleKind.TypeReference:
                    return ReferenceName((TypeReferenceHandle)token);
                case HandleKind.TypeSpecification:
                    return reader.GetTypeSpecification((TypeSpecificationHandle)token).DecodeSignature(Names, null);
                default:
                    throw new InvalidOperationException($"unexpected token kind {token.Kind}");
            }
        }

        private string MethodSignature(BlobHandle signature)
        {
            MethodSignature<string> decoded = new SignatureDecoder<string, object?>(Names, reader, null).DecodeMethodSignature(ref Blob(signature));
            return $"`{decoded.GenericParameterCount}({string.Join(',', decoded.ParameterTypes)}):{decoded.ReturnType}";
        }

        private ref BlobReader Blob(BlobHandle handle)
        {
            signatureBlob = reader.GetBlobReader(handle);
            return ref signatureBlob;
        }

        private string DefinitionName(TypeDefinitionHandle handle)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            string name = Canonical(reader.GetString(type.Name));
            TypeDefinitionHandle declaring = type.GetDeclaringType();
            if (!declaring.IsNil)
            {
                return $"{DefinitionName(declaring)}+{name}";
            }

            string space = reader.GetString(type.Namespace);
            return space.Length == 0 ? name : $"{space}.{name}";
        }

        private string ReferenceName(TypeReferenceHandle handle)
        {
            TypeReference type = reader.GetTypeReference(handle);
            string name = $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";
            return type.ResolutionScope.Kind switch
            {
                HandleKind.TypeReference => $"{ReferenceName((TypeReferenceHandle)type.ResolutionScope)}+{reader.GetString(type.Name)}",
                HandleKind.AssemblyReference => $"[{AssemblyName((AssemblyReferenceHandle)type.ResolutionScope)}]{name}",
                _ => name,
            };
        }

        private string AssemblyName(AssemblyReferenceHandle handle)
        {
            AssemblyReference assembly = reader.GetAssemblyReference(handle);
            string name = reader.GetString(assembly.Name);
            return repositoryAssemblies.Contains(name) ? name : $"{name}@{assembly.Version}";
        }

        /// <summary>A compiler-generated name (<c>&lt;Test&gt;d__5</c>, <c>&lt;&gt;c__DisplayClass5_0</c>) as its order in this walk.</summary>
        private string Canonical(string name)
        {
            if (!name.Contains('<', StringComparison.Ordinal))
            {
                return name;
            }

            if (!generated.TryGetValue(name, out string? canonical))
            {
                canonical = $"<gen{generated.Count}>";
                generated[name] = canonical;
            }

            return canonical;
        }

        private TypeDefinitionHandle? FindType(string className) =>
            reader.TypeDefinitions.Cast<TypeDefinitionHandle?>().FirstOrDefault(handle => PlainName(handle!.Value) == className);

        private string PlainName(TypeDefinitionHandle handle)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            string name = reader.GetString(type.Name);
            TypeDefinitionHandle declaring = type.GetDeclaringType();
            if (!declaring.IsNil)
            {
                return $"{PlainName(declaring)}+{name}";
            }

            string space = reader.GetString(type.Namespace);
            return space.Length == 0 ? name : $"{space}.{name}";
        }

        /// <summary>The methods named <paramref name="name"/> on the type or, when it has none, on its nearest base in this assembly.</summary>
        private MethodDefinitionHandle[] MethodsNamed(TypeDefinitionHandle type, string name)
        {
            for (TypeDefinitionHandle? current = type; current is TypeDefinitionHandle currentType; current = BaseInAssembly(currentType))
            {
                MethodDefinitionHandle[] found = [.. reader.GetTypeDefinition(currentType).GetMethods().Where(handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == name)];
                if (found.Length > 0 || name is ".ctor" or ".cctor")
                {
                    return found;
                }
            }

            return [];
        }

        private TypeDefinitionHandle? BaseInAssembly(TypeDefinitionHandle handle)
        {
            EntityHandle baseType = reader.GetTypeDefinition(handle).BaseType;
            return !baseType.IsNil && baseType.Kind == HandleKind.TypeDefinition ? (TypeDefinitionHandle)baseType : null;
        }

        /// <summary>The methods of this assembly a token names, so the walk follows helpers and generic types defined here.</summary>
        private IEnumerable<MethodDefinitionHandle> OwnMethods(EntityHandle token)
        {
            switch (token.Kind)
            {
                case HandleKind.MethodDefinition:
                    return [(MethodDefinitionHandle)token];
                case HandleKind.MethodSpecification:
                    return OwnMethods(reader.GetMethodSpecification((MethodSpecificationHandle)token).Method);
                case HandleKind.MemberReference:
                    MemberReference member = reader.GetMemberReference((MemberReferenceHandle)token);
                    if (member.Parent.Kind != HandleKind.TypeSpecification)
                    {
                        return [];
                    }

                    BlobReader signature = reader.GetBlobReader(reader.GetTypeSpecification((TypeSpecificationHandle)member.Parent).Signature);
                    if (signature.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
                    {
                        return [];
                    }

                    signature.ReadSignatureTypeCode();
                    EntityHandle generic = signature.ReadTypeHandle();
                    string name = reader.GetString(member.Name);
                    return generic.Kind == HandleKind.TypeDefinition
                        ? reader.GetTypeDefinition((TypeDefinitionHandle)generic).GetMethods().Where(handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == name)
                        : [];
                default:
                    return [];
            }
        }

        private static int OneByteOperandSize(byte code) => code switch
        {
            >= 0x0E and <= 0x13 => 1,
            0x1F => 1,
            0x20 or 0x22 => 4,
            0x21 or 0x23 => 8,
            0x29 => 4,
            >= 0x2B and <= 0x37 => 1,
            >= 0x38 and <= 0x44 => 4,
            0xDD => 4,
            0xDE => 1,
            _ => 0,
        };

        private static int TwoByteOperandSize(byte code) => code switch
        {
            >= 0x09 and <= 0x0E => 2,
            0x12 or 0x19 => 1,
            _ => 0,
        };
    }
}
