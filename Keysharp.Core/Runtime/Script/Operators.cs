using Keysharp.Builtins;

namespace Keysharp.Runtime;

// These identifiers are compiler/runtime contracts, not script-visible enum values.
public enum OperatorKind
{
	Add, Subtract, Multiply, Divide, FloorDivide, Power, BitwiseAnd, BitwiseOr, BitwiseXor, BitShiftLeft, BitShiftRight, LogicalBitShiftRight, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual, ValueEquality, IdentityEquality, ValueInequality, IdentityInequality, Concat, RegEx, NotRegEx, Plus, Minus, BitwiseNot, LogicalNot, TruthTest, Increment, Decrement, Count
}

public delegate object BinaryOperator(object left, object right);

public readonly record struct OperatorDefinition(OperatorKind Kind, BinaryOperator Implementation);
public readonly record struct OperatorInfo(OperatorKind Kind, string Symbol, int Arity, string Description, BinaryOperator Builtin, string RequiredPartner = null);

public readonly struct OperatorDeclaration
{
	internal Type Type { get; }
	internal OperatorDefinition[] Instance { get; }
	internal OperatorDefinition[] Static { get; }

	public OperatorDeclaration(Type type, OperatorDefinition[] instance = null, OperatorDefinition[] @static = null)
	{
		Type = type ?? throw new ArgumentNullException(nameof(type));
		Instance = Validate(instance, nameof(instance));
		Static = Validate(@static, nameof(@static));
	}

	private static OperatorDefinition[] Validate(OperatorDefinition[] definitions, string paramName)
	{
		if (definitions == null || definitions.Length == 0) return [];

		var copy = (OperatorDefinition[])definitions.Clone();
		Span<bool> seen = stackalloc bool[(int)OperatorKind.Count];
		seen.Clear();
		foreach (var definition in copy)
		{
			if ((uint)definition.Kind >= (uint)OperatorKind.Count)
				throw new ArgumentOutOfRangeException(paramName, definition.Kind, "Unknown operator kind.");
			if (definition.Implementation == null)
				throw new ArgumentException($"Operator '{definition.Kind}' has no implementation.", paramName);
			if (seen[(int)definition.Kind])
				throw new ArgumentException($"Operator '{definition.Kind}' is declared more than once.", paramName);
			seen[(int)definition.Kind] = true;
		}
		return copy;
	}
}

/// <summary>Immutable operator declarations emitted by a compiled script.</summary>
public sealed class OperatorManifest
{
	private readonly Dictionary<Type, OperatorDeclaration> byType;
	internal OperatorDeclaration[] Declarations { get; }

	public OperatorManifest(params OperatorDeclaration[] declarations)
	{
		if (declarations == null) throw new ArgumentNullException(nameof(declarations));

		Declarations = (OperatorDeclaration[])declarations.Clone();
		byType = new Dictionary<Type, OperatorDeclaration>(Declarations.Length);
		foreach (var declaration in Declarations)
		{
			if (declaration.Type == null)
				throw new ArgumentException("Operator declarations must identify a type.", nameof(declarations));
			if (!byType.TryAdd(declaration.Type, declaration))
				throw new ArgumentException($"Type '{declaration.Type}' has more than one operator declaration.", nameof(declarations));
		}
	}

	internal bool TryGet(Type type, out OperatorDeclaration declaration) => byType.TryGetValue(type, out declaration);
}

public static class OperatorCatalog
{
	private static readonly OperatorInfo[] all =
	{
		new(OperatorKind.Add, "+", 2, "addition", Script.Add),
		new(OperatorKind.Subtract, "-", 2, "subtraction", Script.Subtract),
		new(OperatorKind.Multiply, "*", 2, "multiplication", Script.Multiply),
		new(OperatorKind.Divide, "/", 2, "division", Script.Divide),
		new(OperatorKind.FloorDivide, "//", 2, "floor divide", Script.FloorDivide),
		new(OperatorKind.Power, "**", 2, "power", Script.Power),
		new(OperatorKind.BitwiseAnd, "&", 2, "bitwise and", Script.BitwiseAnd),
		new(OperatorKind.BitwiseOr, "|", 2, "bitwise or", Script.BitwiseOr),
		new(OperatorKind.BitwiseXor, "^", 2, "bitwise xor", Script.BitwiseXor),
		new(OperatorKind.BitShiftLeft, "<<", 2, "arithmetic left shift", Script.BitShiftLeft),
		new(OperatorKind.BitShiftRight, ">>", 2, "arithmetic right shift", Script.BitShiftRight),
		new(OperatorKind.LogicalBitShiftRight, ">>>", 2, "logical right shift", Script.LogicalBitShiftRight),
		new(OperatorKind.LessThan, "<", 2, "less than", Script.LessThan),
		new(OperatorKind.LessThanOrEqual, "<=", 2, "less than or equal", Script.LessThanOrEqual),
		new(OperatorKind.GreaterThan, ">", 2, "greater than", Script.GreaterThan),
		new(OperatorKind.GreaterThanOrEqual, ">=", 2, "greater than or equal", Script.GreaterThanOrEqual),
		new(OperatorKind.ValueEquality, "=", 2, "equality", Script.ValueEquality, "!="),
		new(OperatorKind.IdentityEquality, "==", 2, "case-sensitive equality", Script.IdentityEquality, "!=="),
		new(OperatorKind.ValueInequality, "!=", 2, "inequality", Script.ValueInequality, "="),
		new(OperatorKind.IdentityInequality, "!==", 2, "case-sensitive inequality", Script.IdentityInequality, "=="),
		new(OperatorKind.Concat, ".", 2, "concat", Script.Concat),
		new(OperatorKind.RegEx, "~=", 2, "regex match", Script.RegEx),
		new(OperatorKind.NotRegEx, "!~=", 2, "regex not match", Script.NotRegEx),
		new(OperatorKind.Plus, "+", 1, "unary plus", (value, _) => Script.Plus(value)),
		new(OperatorKind.Minus, "-", 1, "subtraction or minus", (value, _) => Script.Minus(value)),
		new(OperatorKind.BitwiseNot, "~", 1, "bitwise not", (value, _) => Script.BitwiseNot(value)),
		new(OperatorKind.LogicalNot, "!", 1, "logical not", (value, _) => Script.LogicalNot(value)),
		new(OperatorKind.TruthTest, "?", 1, "truth test", (value, _) => Script.ForceBool(value)),
		new(OperatorKind.Increment, "++", 1, "increment", (value, _) => Script.Increment(value)),
		new(OperatorKind.Decrement, "--", 1, "decrement", (value, _) => Script.Decrement(value)),
	};
	public static IReadOnlyList<OperatorInfo> All { get; } = System.Array.AsReadOnly(all);

	static OperatorCatalog()
	{
		if (all.Length != (int)OperatorKind.Count)
			throw new InvalidOperationException("Operator catalog count does not match OperatorKind.");

		var signatures = new HashSet<(string Symbol, int Arity)>();
		for (var i = 0; i < all.Length; i++)
		{
			var op = all[i];
			if ((int)op.Kind != i)
				throw new InvalidOperationException($"Operator catalog entry {i} is {op.Kind}.");
			if (!signatures.Add((op.Symbol, op.Arity)))
				throw new InvalidOperationException($"Duplicate operator catalog signature '{op.Symbol}'/{op.Arity}.");
		}

		foreach (var op in all)
			if (op.RequiredPartner is { } partner && !signatures.Contains((partner, op.Arity)))
				throw new InvalidOperationException($"Operator '{op.Symbol}' requires missing partner '{partner}'.");
	}

	public static bool Supports(string symbol) => All.Any(op => op.Symbol == symbol);
	public static bool TryResolve(string symbol, int arity, out OperatorKind kind)
	{
		foreach (var op in All)
			if (op.Symbol == symbol && op.Arity == arity) { kind = op.Kind; return true; }
		kind = default;
		return false;
	}
	public static OperatorInfo Get(OperatorKind kind) => All[(int)kind];
}

/// <summary>A class's immutable operator slots, including inherited implementations.</summary>
internal sealed class OperatorTable
{
	public static readonly OperatorTable Empty = new(null);
	private readonly BinaryOperator[] slots;

	public OperatorTable(OperatorTable inherited, params OperatorDefinition[] declarations)
	{
		slots = inherited == null ? new BinaryOperator[(int)OperatorKind.Count] : (BinaryOperator[])inherited.slots.Clone();
		foreach (var declaration in declarations) slots[(int)declaration.Kind] = declaration.Implementation;
	}
	internal BinaryOperator this[OperatorKind kind] => slots[(int)kind];
	internal static OperatorTable Apply(OperatorTable inherited, OperatorDefinition[] declarations) =>
		declarations.Length == 0 ? inherited : new OperatorTable(inherited, declarations);
}

/// <summary>Per-script operator lookup with immutable published tables.</summary>
public sealed class OperatorRegistry
{
	private readonly record struct ClassOperators(OperatorTable Instance, OperatorTable Static);
	private static readonly OperatorTable builtins = new(null,
		OperatorCatalog.All.Select(op => new OperatorDefinition(op.Kind, op.Builtin)).ToArray());
	private static readonly Dictionary<Type, ClassOperators> builtinTables = new()
	{
		[typeof(long)] = new(builtins, OperatorTable.Empty), [typeof(double)] = new(builtins, OperatorTable.Empty),
		[typeof(bool)] = new(builtins, OperatorTable.Empty), [typeof(string)] = new(builtins, OperatorTable.Empty)
	};
	private Dictionary<Type, ClassOperators> tables = builtinTables;
	private readonly object registrationLock = new();
	private OperatorManifest registeredManifest;

	/// <summary>Registers the compiled script's complete operator manifest.</summary>
	public void Register(OperatorManifest manifest)
	{
		ArgumentNullException.ThrowIfNull(manifest);

		lock (registrationLock)
		{
			if (ReferenceEquals(registeredManifest, manifest)) return;
			if (registeredManifest != null)
				throw new InvalidOperationException("A script's operator manifest is already registered.");

			var current = tables;
			foreach (var declaration in manifest.Declarations)
				if (current.ContainsKey(declaration.Type))
					throw new InvalidOperationException($"Operators for type '{declaration.Type}' are already registered.");

			var resolved = new Dictionary<Type, ClassOperators>(manifest.Declarations.Length);
			var visiting = new HashSet<Type>();

			ClassOperators Resolve(Type type)
			{
				if (resolved.TryGetValue(type, out var result)) return result;
				if (!manifest.TryGet(type, out var declaration))
				{
					if (current.TryGetValue(type, out result)) return result;
					return type.BaseType is { } externalBase
						? Resolve(externalBase)
						: new ClassOperators(OperatorTable.Empty, OperatorTable.Empty);
				}
				if (!visiting.Add(type))
					throw new InvalidOperationException($"Cyclic operator inheritance involving type '{type}'.");

				var inherited = type.BaseType is { } baseType
					? Resolve(baseType)
					: new ClassOperators(OperatorTable.Empty, OperatorTable.Empty);
				result = new ClassOperators(
					OperatorTable.Apply(inherited.Instance, declaration.Instance),
					OperatorTable.Apply(inherited.Static, declaration.Static));
				visiting.Remove(type);
				resolved.Add(type, result);
				return result;
			}

			Dictionary<Type, ClassOperators> updated = null;
			foreach (var declaration in manifest.Declarations)
			{
				var entry = Resolve(declaration.Type);
				if (entry.Instance == OperatorTable.Empty && entry.Static == OperatorTable.Empty) continue;
				updated ??= new Dictionary<Type, ClassOperators>(tables);
				updated.Add(declaration.Type, entry);
			}

			registeredManifest = manifest;
			if (updated != null) Volatile.Write(ref tables, updated);
		}
	}

	internal void RegisterAlias(Type alias, Type source)
	{
		if (alias == source) return;

		lock (registrationLock)
		{
			var current = tables;
			if (!current.TryGetValue(source, out var entry)) return;
			if (current.TryGetValue(alias, out var existing))
			{
				if (existing != entry) throw new InvalidOperationException("Class operators cannot be replaced.");
				return;
			}

			var updated = new Dictionary<Type, ClassOperators>(current) { [alias] = entry };
			Volatile.Write(ref tables, updated);
		}
	}

	public bool TryInvoke(OperatorKind kind, object left, object right, out object result)
	{
		var classType = (left as Class)?.OperatorType;
		if (left != null && Volatile.Read(ref tables).TryGetValue(classType ?? left.GetType(), out var entry))
		{
			var table = classType != null ? entry.Static : entry.Instance;
			if (table[kind] is { } implementation)
			{
				result = implementation(left, right);
				if (result == null && table != builtins)
					result = Errors.UnsetErrorOccurred($"Result of {OperatorCatalog.Get(kind).Description}", Script.DefaultObject);
				return true;
			}
		}
		result = null;
		return false;
	}

	public object Invoke(OperatorKind kind, object left, object right)
	{
		if (left == null) return Errors.UnsetErrorOccurred($"Left side operand of {OperatorCatalog.Get(kind).Description}", Script.DefaultObject);
		if (right == null && OperatorCatalog.Get(kind).Arity == 2)
			return Errors.UnsetErrorOccurred($"Right side operand of {OperatorCatalog.Get(kind).Description}", Script.DefaultObject);
		return TryInvoke(kind, left, right, out var result) ? result : Errors.TypeErrorOccurred(left, typeof(double), Script.DefaultObject);
	}
}
