using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Lockflight.Cli.Lifecycle.RedGreen;

namespace Lockflight.Cli.Ci;

/// <summary>
/// The per-method fingerprint of a test, read from a built test assembly without loading it (AOT-safe): a SHA-256 over the IL of
/// the test method and of every method of the test assembly it reaches (helpers in any class, base classes, lambdas, local functions,
/// async and iterator state machines), its class's constructors, set-up and tear-down methods and the module initializer, with every metadata token written as the
/// name it stands for, each method's exception clauses and local variable types, the test's attributes (<c>InlineData</c> values,
/// traits, <c>Skip</c>), its class's and base classes' attributes and base types (a generic base with its type arguments), the
/// assembly's own attributes, and the code of the fixtures they name (a collection's or the assembly's fixtures, an attribute
/// type of the assembly). Compiler-generated names are
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
    private const byte LdVirtFtnOpCode = 0x07;

    private static readonly string[] CompilerAttributes = ["]System.Runtime.CompilerServices.", "]System.Diagnostics."];

    private static readonly HashSet<string> EmbeddedAttributes = new(StringComparer.Ordinal)
    {
        "Microsoft.CodeAnalysis.EmbeddedAttribute",
        "System.Runtime.CompilerServices.NullableAttribute",
        "System.Runtime.CompilerServices.NullableContextAttribute",
        "System.Runtime.CompilerServices.NullablePublicOnlyAttribute",
        "System.Runtime.CompilerServices.RefSafetyRulesAttribute",
        "System.Runtime.CompilerServices.IsReadOnlyAttribute",
        "System.Runtime.CompilerServices.IsByRefLikeAttribute",
        "System.Runtime.CompilerServices.IsUnmanagedAttribute",
        "System.Runtime.CompilerServices.NativeIntegerAttribute",
        "System.Runtime.CompilerServices.ScopedRefAttribute",
        "System.Runtime.CompilerServices.RequiresLocationAttribute",
        "System.Runtime.CompilerServices.ParamCollectionAttribute",
    };
    private static readonly HashSet<byte> MethodOps = [0x27, 0x28, 0x6F, 0x73];
    private static readonly HashSet<byte> TokenOps = [0x70, 0x71, 0x74, 0x75, 0x79, 0x7B, 0x7C, 0x7D, 0x7E, 0x7F, 0x80, 0x81, 0x8C, 0x8D, 0x8F, 0xA3, 0xA4, 0xA5, 0xC2, 0xC6, 0xD0];
    private static readonly HashSet<byte> TwoByteMethodOps = [0x06, 0x07];
    private static readonly HashSet<byte> TwoByteTokenOps = [0x15, 0x16, 0x1C];

    private static readonly string[] LifecycleAttributes = ["SetUp", "TearDown", "TestInitialize", "TestCleanup", "ClassInitialize", "ClassCleanup", "AssemblyInitialize", "AssemblyCleanup"];
    private static readonly string[] LifecycleMethods = ["InitializeAsync", "DisposeAsync", "Dispose"];

    private readonly PEReader pe = new(File.OpenRead(assemblyPath));
    private Dictionary<string, List<MethodDefinitionHandle>>? virtualsByName;

    /// <summary>The fingerprint of <paramref name="className"/>.<paramref name="method"/>; null when it cannot be computed (counts as changed).</summary>
    public string? Fingerprint(string className, string method) => CanonicalText(className, method) is string text
        ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
        : null;

    /// <summary>
    /// How many call sites in the test's walk call an assertion (<see cref="IsAssertion"/>); null when the walk cannot be read. A
    /// static count, compared between two versions of one test: an amend that has fewer dropped an assertion.
    /// </summary>
    public int? Assertions(string className, string method) => CanonicalText(className, method) is string text ? CountAssertions(text) : null;

    /// <summary>The call sites (<c>call</c>, <c>callvirt</c>) in a canonical text whose target is an assertion.</summary>
    public static int CountAssertions(string canonicalText) =>
        canonicalText.Split('\n').Where(line => !line.StartsWith("method ", StringComparison.Ordinal) && !line.StartsWith("attr ", StringComparison.Ordinal))
            .SelectMany(line => line.Split(';'))
            .Count(instruction => (instruction.StartsWith("28 ", StringComparison.Ordinal) || instruction.StartsWith("6f ", StringComparison.Ordinal)) && IsAssertion(instruction[3..]));

    /// <summary>
    /// Whether a call target (<c>[xunit.assert 2.9]Xunit.Assert::Equal`1(…)</c>) is an assertion: a member of a type whose name holds
    /// <c>Assert</c> or <c>Expect</c> (xUnit, NUnit and MSTest asserts, a repository's own <c>Expect</c> helper), or a method named
    /// <c>Should…</c> or <c>Verify…</c>, or holding <c>Assert</c> (Shouldly, FluentAssertions, mocking verifications).
    /// </summary>
    private static bool IsAssertion(string target)
    {
        int separator = target.IndexOf("::", StringComparison.Ordinal);
        if (separator < 0)
        {
            return false;
        }

        string parent = target[..separator];
        int generic = parent.IndexOf('<', StringComparison.Ordinal);
        parent = generic < 0 ? parent : parent[..generic];
        string type = parent[(parent.LastIndexOfAny(['.', '+', ']']) + 1)..];
        string rest = target[(separator + 2)..];
        int end = rest.IndexOfAny(['`', '(', '<']);
        string method = end < 0 ? rest : rest[..end];
        return type.Contains("Assert", StringComparison.Ordinal) || type.Contains("Expect", StringComparison.Ordinal)
            || method.StartsWith("Should", StringComparison.Ordinal) || method.StartsWith("Verify", StringComparison.Ordinal) || method.Contains("Assert", StringComparison.Ordinal);
    }

    /// <summary>The text the fingerprint hashes, for a person comparing two fingerprints; null when it cannot be computed.</summary>
    public string? CanonicalText(string className, string method)
    {
        try
        {
            return new Walk(pe, repositoryAssemblies, VirtualsByName()).Run(className, method);
        }
        catch (Exception exception) when (exception is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    public void Dispose() => pe.Dispose();

    /// <summary>
    /// The assembly's virtual methods by name, built once for every test's walk; an explicit interface implementation
    /// (<c>Ns.IClock.Now</c>) is listed under the member's own name too.
    /// </summary>
    private Dictionary<string, List<MethodDefinitionHandle>> VirtualsByName()
    {
        if (virtualsByName is null)
        {
            MetadataReader reader = pe.GetMetadataReader();
            virtualsByName = new Dictionary<string, List<MethodDefinitionHandle>>(StringComparer.Ordinal);
            foreach (MethodDefinitionHandle handle in reader.MethodDefinitions)
            {
                MethodDefinition method = reader.GetMethodDefinition(handle);
                if ((method.Attributes & MethodAttributes.Virtual) == 0)
                {
                    continue;
                }

                string name = reader.GetString(method.Name);
                foreach (string key in new[] { name, name[(name.LastIndexOf('.') + 1)..] }.Distinct(StringComparer.Ordinal))
                {
                    if (!virtualsByName.TryGetValue(key, out List<MethodDefinitionHandle>? methods))
                    {
                        methods = [];
                        virtualsByName[key] = methods;
                    }

                    methods.Add(handle);
                }
            }
        }

        return virtualsByName;
    }

    /// <summary>One fingerprint's walk: the canonical text it builds and the generated names it has seen, in order.</summary>
    private sealed class Walk(PEReader pe, IReadOnlySet<string> repositoryAssemblies, Dictionary<string, List<MethodDefinitionHandle>> virtualsByName)
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
                // The compiler's own attributes name generated types with their ordinals (AsyncStateMachine(typeof(<T>d__5))) and
                // are left out; an attribute type of this assembly (a before/after hook, a data source) brings in its code.
                Attribute(reader.GetCustomAttribute(handle), "attr ");
            }

            pending.Enqueue(roots[0]);
            EnqueueFrameworkCode(type, test);
            ClassAndAssembly(type);
            for (TypeDefinitionHandle? current = type; current is TypeDefinitionHandle currentType; current = BaseInAssembly(currentType))
            {
                foreach (MethodDefinitionHandle constructor in MethodsNamed(currentType, ".ctor").Concat(MethodsNamed(currentType, ".cctor")))
                {
                    pending.Enqueue(constructor);
                }

                // What the framework runs around every test of the class: set-up and tear-down methods, IAsyncLifetime, IDisposable.
                foreach (MethodDefinitionHandle handle in reader.GetTypeDefinition(currentType).GetMethods().Where(IsLifecycle))
                {
                    pending.Enqueue(handle);
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
        /// What the test framework reads off the class and the assembly: each class's attributes and base type up the chain (a
        /// <c>[Collection]</c> also brings in its definition's fixtures), and the assembly's attributes outside <c>System.</c>
        /// (an assembly fixture, a test framework or orderer); an attribute type of this assembly, or a type of this assembly an
        /// attribute names, brings in all of its code.
        /// </summary>
        private void ClassAndAssembly(TypeDefinitionHandle type)
        {
            for (TypeDefinitionHandle? current = type; current is TypeDefinitionHandle currentType; current = BaseInAssembly(currentType))
            {
                TypeDefinition definition = reader.GetTypeDefinition(currentType);
                text.Append("class ").Append(DefinitionName(currentType)).Append(" : ").Append(definition.BaseType.IsNil ? "-" : Token(definition.BaseType)).Append('\n');

                // A type of this assembly a generic base is built with (Base<Calculator>) runs through it: new T(), T.Create().
                foreach (TypeDefinitionHandle argument in GenericTypes.OwnTypes(reader, definition.BaseType))
                {
                    EnqueueAll(argument, name: null);
                }

                foreach (CustomAttributeHandle handle in definition.GetCustomAttributes())
                {
                    Attribute(reader.GetCustomAttribute(handle), "class-attr ");
                }
            }

            foreach (CustomAttributeHandle handle in reader.GetAssemblyDefinition().GetCustomAttributes())
            {
                CustomAttribute attribute = reader.GetCustomAttribute(handle);
                string constructor = Token(attribute.Constructor);
                if (!constructor[..Math.Max(0, constructor.IndexOf("::", StringComparison.Ordinal))].Contains("]System.", StringComparison.Ordinal))
                {
                    Attribute(attribute, "assembly-attr ");
                }
            }
        }

        /// <summary>
        /// An attribute the compiler writes, not the person: one of <c>System.Runtime.CompilerServices</c> or
        /// <c>System.Diagnostics</c> from another assembly, or a type the compiler embedded in this one (<c>NullableContext</c>,
        /// <c>Nullable</c>, <c>RefSafetyRules</c> and the like, marked <c>[Microsoft.CodeAnalysis.Embedded]</c>). The nullable ones
        /// follow the majority of a class's members, so a member added elsewhere in the class would change a test's fingerprint.
        /// </summary>
        private static bool IsCompilerAttribute(string name) =>
            name[..Math.Max(0, name.IndexOf("::", StringComparison.Ordinal))] is string declaring
            && CompilerAttributes.Any(prefix => declaring.Contains(prefix, StringComparison.Ordinal));

        /// <summary>
        /// One of the attribute types the compiler embeds in an assembly, by its full name, deriving from <c>System.Attribute</c>
        /// with no interface and marked <c>[Microsoft.CodeAnalysis.Embedded]</c>; a person's type with such a name is still walked.
        /// </summary>
        private bool IsEmbedded(TypeDefinitionHandle type)
        {
            const string Embedded = "Microsoft.CodeAnalysis.EmbeddedAttribute";
            TypeDefinition definition = reader.GetTypeDefinition(type);
            string name = DefinitionName(type);
            if (!EmbeddedAttributes.Contains(name) || definition.GetInterfaceImplementations().Count > 0
                || definition.BaseType.IsNil || !Token(definition.BaseType).EndsWith("]System.Attribute", StringComparison.Ordinal))
            {
                return false;
            }

            return name == Embedded || definition.GetCustomAttributes()
                .Any(handle => Token(reader.GetCustomAttribute(handle).Constructor).StartsWith($"{Embedded}::", StringComparison.Ordinal));
        }

        /// <summary>Appends an attribute with its arguments and follows the code of this assembly it names.</summary>
        private void Attribute(CustomAttribute attribute, string kind)
        {
            string constructor = Token(attribute.Constructor);
            if (IsCompilerAttribute(constructor))
            {
                return;
            }

            if (attribute.Constructor.Kind == HandleKind.MethodDefinition
                && reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType() is var attributeType)
            {
                // An embedded one is left out whole: where the compiler places it follows the class's other members, and its code
                // (a constructor storing its argument) only runs when something reads it by reflection.
                if (IsEmbedded(attributeType))
                {
                    return;
                }

                EnqueueAll(attributeType, name: null);
            }

            CustomAttributeValue<string> value = attribute.DecodeValue(new AttributeTypes(Names));
            text.Append(kind).Append(constructor).Append(' ').Append(AttributeValue(attribute)).Append('\n');

            // A generic attribute ([Collection<Db>], [ClassData<Rows>]) carries its type in the constructor's parent.
            if (attribute.Constructor.Kind == HandleKind.MemberReference)
            {
                foreach (TypeDefinitionHandle argument in GenericTypes.OwnTypes(reader, reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent))
                {
                    EnqueueAll(argument, name: null);
                    EnqueueFixtures(argument);
                }
            }

            bool collection = constructor.Contains("CollectionAttribute", StringComparison.Ordinal);
            foreach (CustomAttributeTypedArgument<string> argument in value.FixedArguments.Concat(value.NamedArguments.Select(named => new CustomAttributeTypedArgument<string>(named.Type, named.Value))))
            {
                if (argument.Value is not string named)
                {
                    continue;
                }

                if (new AttributeTypes(Names).IsSystemType(argument.Type) && FindType(named.Split(',')[0].Trim()) is TypeDefinitionHandle own)
                {
                    EnqueueAll(own, name: null);
                    EnqueueFixtures(own);
                }
                else if (collection)
                {
                    foreach (TypeDefinitionHandle definition in CollectionDefinitions(named))
                    {
                        EnqueueFixtures(definition);
                    }
                }
            }
        }

        /// <summary>The types of this assembly marked <c>[CollectionDefinition(name)]</c>.</summary>
        private IEnumerable<TypeDefinitionHandle> CollectionDefinitions(string name) =>
            reader.TypeDefinitions.Where(candidate => reader.GetTypeDefinition(candidate).GetCustomAttributes().Select(reader.GetCustomAttribute).Any(attribute =>
                Token(attribute.Constructor).Contains("CollectionDefinitionAttribute", StringComparison.Ordinal)
                && attribute.DecodeValue(new AttributeTypes(Names)).FixedArguments.Any(argument => argument.Value is string text && text == name)));

        private bool IsLifecycle(MethodDefinitionHandle handle)
        {
            MethodDefinition method = reader.GetMethodDefinition(handle);
            string name = reader.GetString(method.Name);
            return LifecycleMethods.Any(lifecycle => name == lifecycle || name.EndsWith("." + lifecycle, StringComparison.Ordinal))
                || method.GetCustomAttributes().Any(attribute => Token(reader.GetCustomAttribute(attribute).Constructor) is string constructor
                    && LifecycleAttributes.Any(marker => constructor.Contains($"{marker}Attribute::", StringComparison.Ordinal)));
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

            MethodBodyBlock body = pe.GetMethodBody(definition.RelativeVirtualAddress);
            if (!body.LocalSignature.IsNil)
            {
                text.Append("locals ").Append(string.Join(',', reader.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(Names, null))).Append('\n');
            }

            foreach (ExceptionRegion region in body.ExceptionRegions)
            {
                text.Append("region ").Append(region.Kind).Append(' ').Append(region.TryOffset).Append('+').Append(region.TryLength)
                    .Append(' ').Append(region.HandlerOffset).Append('+').Append(region.HandlerLength)
                    .Append(' ').Append(region.CatchType.IsNil ? "-" : Token(region.CatchType))
                    .Append(' ').Append(region.FilterOffset).Append('\n');
            }

            BlobReader il = body.GetILReader();
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
                        EntityHandle token = MetadataTokens.EntityHandle(il.ReadInt32());
                        Operand(token);
                        if (second == LdVirtFtnOpCode)
                        {
                            EnqueueOverrides(token);
                        }
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

            foreach (MethodDefinitionHandle handle in virtualsByName.GetValueOrDefault(name) ?? [])
            {
                if (framework is null || DerivesFrom(reader.GetMethodDefinition(handle).GetDeclaringType(), framework))
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

            EnqueueFixtures(type);
        }

        /// <summary>The fixtures a type takes through generic interfaces (<c>IClassFixture&lt;T&gt;</c>, <c>ICollectionFixture&lt;T&gt;</c>), up its bases.</summary>
        private void EnqueueFixtures(TypeDefinitionHandle type)
        {
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

        /// <summary>The base type when this assembly defines it, a generic base (<c>PriceTestsBase&lt;decimal&gt;</c>) by its definition.</summary>
        private TypeDefinitionHandle? BaseInAssembly(TypeDefinitionHandle handle) => GenericTypes.Definition(reader, reader.GetTypeDefinition(handle).BaseType);

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
