using Keysharp.Builtins;

namespace Keysharp.Runtime
{
	public class Variables
	{
		private readonly Script script;

		public LazyDictionary<Type, Prototype> Prototypes = new();
		public LazyDictionary<Type, Class> Statics = new();
		internal List<(string, bool)> preloadedDlls = [];
		internal DateTime startTime = DateTime.UtcNow;
		private readonly ConcurrentDictionary<Type, ModuleScope> modules = new();
		// Physical members use (declaring type, CLR name); classes use (class type, null).
		internal readonly ConcurrentDictionary<(Type, string), VarRef> VariableRefs = new();
		private readonly Type[] programModules;
		// Defensive fallback for a script with no modules at all (the generated program always has the main module,
		// so this is effectively never used).
		private Dictionary<string, ScriptVar> programVars;
		// The variable store for the module currently executing on this thread (the main module when none is active,
		// e.g. on the UI thread).
		internal Dictionary<string, ScriptVar> GlobalVars => GetModuleVars(script.CurrentModuleType);
		// All modules and their global-variable stores, in declaration order, for ListVars.
		internal IEnumerable<KeyValuePair<Type, KeyValuePair<string, ScriptVar>[]>> AllModuleVars =>
			programModules.Select(type => KeyValuePair.Create(type, modules[type].OriginalFields));
		private readonly Type defaultModuleType;
		private readonly Type ahkModuleType;
		internal Type DefaultModuleType => defaultModuleType;

		internal Variables(Script script)
		{
			this.script = script ?? throw new ArgumentNullException(nameof(script));
			var flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
			programModules = script.ProgramType?.GetNestedTypes(flags).Where(IsModuleType).ToArray() ?? [];
			ahkModuleType = programModules.FirstOrDefault(type => type.Name == "AHK");

			foreach (var type in programModules)
				modules[type] = new(type);

			if (programModules.Length == 0)
				return;

			defaultModuleType = programModules.FirstOrDefault(t => t.Name.Equals(Keywords.MainModuleName, StringComparison.OrdinalIgnoreCase)) ?? programModules[0];
		}

		private sealed class ModuleScope
		{
			internal readonly Dictionary<string, ScriptVar> Variables;
			internal readonly KeyValuePair<string, ScriptVar>[] OriginalFields;
			internal Lazy<Module> Instance;

			internal ModuleScope(Type type)
			{
				var declared = GatherTypeVariables(type);
				var builtin = type.Assembly == typeof(Module).Assembly;
				Variables = declared.ToDictionary(kv => kv.Key,
					kv => builtin && kv.Value.pi != null ? ScriptVar.Builtin(kv.Value.pi) : ScriptVar.Of(kv.Value), StringComparer.OrdinalIgnoreCase);
				OriginalFields = declared.Where(kv => kv.Value.fi != null)
					.Select(kv => KeyValuePair.Create(kv.Key, ScriptVar.Of(kv.Value))).ToArray();

				// A script module's functions and classes are among its variables, and its other methods are the compiler's own.
				if (builtin)
				{
					foreach (var nested in type.GetNestedTypes(BindingFlags.Public)
						.Where(t => !Struct.IsAutoPointerClass(t) && !IsModuleType(t) && typeof(Any).IsAssignableFrom(t)
							&& !t.IsDefined(typeof(PublicHiddenFromUser), false)))
						Variables[Script.GetUserDeclaredName(nested) ?? nested.Name] = ScriptVar.Class(nested);
					foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
						.Where(m => !m.IsSpecialName && !m.IsDefined(typeof(PublicHiddenFromUser), false)))
						Variables[Script.GetUserDeclaredName(method) ?? method.Name] = ResolveMember(type, method.Name, ModuleBindingKind.Function);
				}

				foreach (var alias in type.GetCustomAttributes<ModuleBindingAttribute>())
				{
					if (string.IsNullOrEmpty(alias.Name) || alias.Owner == null)
						throw new InvalidOperationException($"Invalid module binding on {type.FullName}.");
					Variables[alias.Name] = ResolveMember(alias.Owner, alias.Member, alias.Kind);
				}
			}
		}

		[Flags]
		internal enum VariableType
		{
			Field = 1,
			Property = 2,
			NormalName = 4,
			SpecialName = 8,
		}

		internal static Dictionary<string, MethodPropertyHolder> GatherTypeVariables(Type t, VariableType vartypes = VariableType.Field | VariableType.Property | VariableType.NormalName, string funcName = null)
		{
			// Public only. The lowerer emits a module's variables, functions, classes and SL_ static-local fields `public static`,
			// and a field caching a built-in it uses `private`, which is no script variable. NonPublic would also make C#
			// accessibility meaningless inside a `#CSharp` block, where `private static long[] scratch` would become a script global.
			var flags = BindingFlags.Static | BindingFlags.Public;
			PropertyInfo[] props = null;
			FieldInfo[] fields = null;

			if (vartypes.HasFlag(VariableType.Field))
				fields = t.GetFields(flags);
			if (vartypes.HasFlag(VariableType.Property))
				props = t.GetProperties(flags);

			var vars = new Dictionary<string, MethodPropertyHolder>((fields?.Length ?? 0) + (props?.Length ?? 0), StringComparer.OrdinalIgnoreCase);

			bool wantnormal = vartypes.HasFlag(VariableType.NormalName);
			bool wantspecial = vartypes.HasFlag(VariableType.SpecialName);

			if (vartypes.HasFlag(VariableType.Field))
			{
				// A member's C# name decides what it is, and the name it is known by is its declared spelling where it has one.
				foreach (var field in fields)
				{
					string name = field.Name;
					if (field.IsDefined(typeof(InlineCSharpAttribute), false))
					{
						if (wantspecial) continue;
					}
					else
					{
						if (name.StartsWith("@", StringComparison.Ordinal)) name = name.Substring(1);
						if (name.StartsWith(Keywords.EscapePrefix)) name = name.Substring(Keywords.EscapePrefix.Length);
						if (name.StartsWith(Keywords.StaticLocalFieldPrefix))
						{
							if (wantnormal) continue;
							name = ExtractStaticLocalUserName(name, funcName);
							if (name == null) continue;
						}
						else if (IsSpecialName(name))
						{
							if (wantnormal) continue;
						}
						else if (wantspecial) continue;
					}
					if (field.IsDefined(typeof(PublicHiddenFromUser), false)) continue;
					vars[Script.GetUserDeclaredName(field) ?? name] = MethodPropertyHolder.GetOrAdd(field);
				}
			}

			if (vartypes.HasFlag(VariableType.Property))
			{
				foreach (var prop in props)
				{
					var name = prop.Name;
					if (prop.IsDefined(typeof(InlineCSharpAttribute), false))
					{
						if (wantspecial) continue;
					}
					else
					{
						if (name.StartsWith("@", StringComparison.Ordinal)) name = name.Substring(1);
						bool isSpecial = IsSpecialName(name);
						if (wantspecial && !isSpecial) continue;
						if (wantnormal && isSpecial) continue;
					}
					if (prop.IsDefined(typeof(PublicHiddenFromUser), false)) continue;
					vars[Script.GetUserDeclaredName(prop) ?? name] = MethodPropertyHolder.GetOrAdd(prop);
				}
			}

			return vars;
		}

		internal static bool IsSpecialName(string name) => name.Length > 3 && char.IsAsciiLetterUpper(name[0]) && char.IsAsciiLetterUpper(name[1]) && name[2] == '_';

		private static bool IsModuleType(Type type) =>
			typeof(Module).IsAssignableFrom(type);

		internal Dictionary<string, ScriptVar> GetModuleVars(Type moduleType)
		{
			// A null module means "the global scope": resolve to the main module's store. Only a script with no
			// modules at all (never in practice) has nothing to resolve to; gather from the program type then.
			moduleType ??= defaultModuleType;
			if (moduleType == null)
				return programVars ??= GatherTypeVariables(script.ProgramType).ToDictionary(kv => kv.Key, kv => ScriptVar.Of(kv.Value), StringComparer.OrdinalIgnoreCase);

			return GetModule(moduleType).Variables;
		}

		private ModuleScope GetModule(Type type) => modules.GetOrAdd(type, static t => new(t));

		internal Module ModuleObject(Type type)
		{
			var scope = GetModule(type);
			return LazyInitializer.EnsureInitialized(ref scope.Instance, () => new Lazy<Module>(() =>
			{
				var constructor = type.GetConstructor(Type.EmptyTypes);
				return (Module)(constructor != null ? constructor.Invoke([])
					: type.GetConstructor([typeof(object[])]).Invoke([System.Array.Empty<object>()]));
			})).Value;
		}

		internal static ScriptVar ResolveMember(Type owner, string member, ModuleBindingKind kind)
		{
			const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
			if (owner != null && (kind == ModuleBindingKind.Class || !string.IsNullOrEmpty(member)))
			{
				switch (kind)
				{
					case ModuleBindingKind.Field when owner.GetField(member, flags) is { } field:
						return ScriptVar.Of(MethodPropertyHolder.GetOrAdd(field));
					case ModuleBindingKind.Property when owner.GetProperty(member, flags) is { } property:
						return ScriptVar.Of(MethodPropertyHolder.GetOrAdd(property));
					case ModuleBindingKind.BuiltinVariable when owner.GetProperty(member, flags) is { } builtin:
						return ScriptVar.Builtin(builtin);
					case ModuleBindingKind.Function:
						var declared = owner.GetMethods(flags).FirstOrDefault(candidate => candidate.Name == member && !candidate.IsSpecialName);
						if (declared != null)
						{
							if (declared.IsDefined(typeof(InlineCSharpAttribute), false))
								return ScriptVar.Function(declared);
							var name = Script.GetUserDeclaredName(declared) ?? declared.Name;
							var rd = Script.TheScript.ReflectionsData;
							var method = rd.flatPublicStaticMethods.TryGetValue(name, out var flat) && flat.DeclaringType == owner && flat.Name == member
								? flat : Reflections.FindAndCacheStaticMethod(owner, name, -1)?.mi;
							if (method != null && method.DeclaringType == owner)
								return ScriptVar.Function(method);
						}
						break;
					case ModuleBindingKind.Class when typeof(Any).IsAssignableFrom(owner):
						return ScriptVar.Class(owner);
				}
			}

			throw new InvalidOperationException($"Module binding target {owner?.FullName}.{member} ({kind}) does not exist.");
		}

		internal VarRef MemberReference(Type owner, string member, ModuleBindingKind kind) =>
			VariableRefs.GetOrAdd((owner, kind == ModuleBindingKind.Class ? null : member),
				static (_, target) => ResolveMember(target.owner, target.member, target.kind).CreateReference(target.member ?? target.owner.Name), (owner, member, kind));

		internal static string ExtractStaticLocalUserName(string staticFieldName, string funcName = null)
		{
			var s = staticFieldName.TrimStart('@');

			// Expect: SL_<lenF>_<func>_<var>
			// Example: SL_7_my__func___a

			var start = s.IndexOf(Keywords.StaticLocalFieldPrefix, StringComparison.Ordinal);

			if (start == -1)
				return null;

			int i = start + Keywords.StaticLocalFieldPrefix.Length;

			// read lenF
			int lenF = 0;
			while (i < s.Length && char.IsDigit(s[i]))
			{
				lenF = (lenF * 10) + (s[i] - '0');
				i++;
			}
			if (i >= s.Length || s[i] != '_') return null;
			i++; // skip '_'

			if (i + lenF > s.Length) return null;
			if (funcName != null)
			{
				var funcInField = s.Substring(i, lenF);
				if (!string.Equals(funcInField, funcName, StringComparison.OrdinalIgnoreCase))
					return null; // function name doesn't match
			}
			i += lenF;
			if (i >= s.Length || s[i] != '_') return null;
			i++; // skip '_'

			if (i >= s.Length) return null;

			return s.Substring(i);
		}

		public void InitClasses()
		{
			var anyType = typeof(Any);
			var types = script.ReflectionsData.stringToTypes.Values
				.Where(type => type.IsClass && !type.IsAbstract && anyType.IsAssignableFrom(type));
			types = types.Concat(
				Reflections.GetNestedTypes(types.ToArray())
					.Where(type => type.IsClass && !type.IsAbstract && anyType.IsAssignableFrom(type))
			);

			if (script.ProgramType != null)
			{
				var nested = Reflections.GetNestedTypes(script.ProgramType.GetNestedTypes()).Where(type => type.IsClass && anyType.IsAssignableFrom(type));
				types = types.Concat(nested);
			}
			types = types.Distinct();

			/*
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.IsClass && !type.IsAbstract && anyType.IsAssignableFrom(type));
			*/

			// Initiate necessary base types in specific order
			InitClass(typeof(KeysharpFunc));
			// Need to do this so that KeysharpFunc methods contain themselves in the prototype,
			// meaning a circular reference. This shouldn't prevent garbage collection, but
			// I haven't verified that.
			var fop = Prototypes[typeof(KeysharpFunc)];
			KeysharpFunc.PrototypeCall = fop.op["Call"].Call as KeysharpFunc;
			foreach (var op in fop.op)
			{
				var opm = op.Value;
				if (opm.Value is KeysharpFunc fov && fov != null)
				{
					fov.SetBaseInternal(fop);
				}
				if (opm.Get is KeysharpFunc fog && fog != null)
				{
					fog.SetBaseInternal(fop);
				}
				if (opm.Set is KeysharpFunc fos && fos != null)
				{
					fos.SetBaseInternal(fop);
				}
				if (opm.Call is KeysharpFunc foc && foc != null)
				{
					foc.SetBaseInternal(fop);
				}
			}
			InitClass(typeof(Any));
			InitClass(typeof(KeysharpObject));
			InitClass(typeof(Class));

			// Class.Base == Object
			Statics[typeof(Class)].SetBaseInternal(Statics[typeof(KeysharpObject)]);
			// Any.Base == Class.Prototype
			Statics[typeof(Any)].SetBaseInternal(Prototypes[typeof(Class)]);

			// Remove __New because it's only for internal overrides
			Prototypes[typeof(Any)].op.Remove("__New");

			// Manually define Object static instance prototype property to be the Object prototype
			var ksoStatic = Statics[typeof(KeysharpObject)];
			ksoStatic.DefinePropInternal("Prototype", new OwnPropsDesc(Prototypes[typeof(KeysharpObject)]));
			// Object.Base == Any
			ksoStatic.SetBaseInternal(Statics[typeof(Any)]);

			//KeysharpFunc was initialized when Object wasn't, so define the bases
			Prototypes[typeof(KeysharpFunc)].SetBaseInternal(Prototypes[typeof(KeysharpObject)]);
			Statics[typeof(KeysharpFunc)].SetBaseInternal(Statics[typeof(KeysharpObject)]);

			// Runtime module classes are not reflected as script-visible built-ins, but generated
			// module classes derive from them and therefore need prototype entries.
			InitClass(typeof(Module));
			InitClass(typeof(Ahk));

			// Do not initialize the core types again
			var typesToRemoveSet = new HashSet<Type>(new[]
			{
				typeof(Any),
				typeof(KeysharpFunc),
				typeof(KeysharpObject),
				typeof(Class),
				typeof(Module),
				typeof(Ahk)
			});
			var orderedTypes = types.Where(type => !typesToRemoveSet.Contains(type)).OrderBy(Reflections.GetInheritanceDepth);

			// Lazy-initialize all other classes
			foreach (var t in orderedTypes)
			{
				Script.InitClass(t);
			}
		}

		public bool HasVariable(string key) => HasVariable(script.CurrentModuleType, key);

		public bool HasVariable(Type moduleType, string key) => TryGetGlobal(moduleType, key, out _);

		public object GetVariable(string key) => GetVariable(script.CurrentModuleType, key);

		public object GetVariable(Type moduleType, string key) => TryGetGlobal(moduleType, key, out var v) ? v.Get() : null;

		public object SetVariable(string key, object value) => SetVariable(script.CurrentModuleType, key, value);

		public object SetVariable(Type moduleType, string key, object value)
		{
			if (TryGetModuleWriteTarget(moduleType, key, out var v) && v.RequireWritable(VarUsage.Assign, key))
				v.Set(value);

			return value;
		}

		// A module-level write reaches its own declaration, then a built-in variable.
		internal bool TryGetModuleWriteTarget(Type module, string key, out ScriptVar v) =>
			TryGetModuleVar(module, key, out v) || TryGetBuiltinVar(key, out v);

		// The compiler supplies every effective imported name, so a lookup never traverses an import graph.
		internal bool TryGetModuleVar(Type module, string key, out ScriptVar v)
		{
			v = default;

			return (module ??= defaultModuleType) != null && GetModule(module).Variables.TryGetValue(key, out v);
		}

		internal bool TryGetMember(Type module, string key, out ScriptVar v) => TryGetModuleVar(module, key, out v);

		internal bool TryGetBuiltinVar(string key, out ScriptVar v)
		{
			v = script.ReflectionsData.flatPublicStaticProperties.TryGetValue(key, out var prop) ? ScriptVar.Builtin(prop) : default;
			return v.Exists;
		}

		// AHK holds script-created variables as well as built-in variables, functions and classes.
		internal bool TryGetAhkMember(string key, out ScriptVar v) =>
			ahkModuleType != null && TryGetModuleVar(ahkModuleType, key, out v)
			|| TryGetBuiltinVar(key, out v) || TryGetBuiltinFunction(key, out v) || TryGetGlobalClass(key, out v);

		// What a global name finds from a module: its effective binding, then an AHK name.
		internal bool TryGetGlobal(Type module, string key, out ScriptVar v) =>
			TryGetModuleVar(module, key, out v) || TryGetAhkMember(key, out v);

		private bool TryGetBuiltinFunction(string key, out ScriptVar v)
		{
			v = script.ReflectionsData.flatPublicStaticMethods.TryGetValue(key, out var method) ? ScriptVar.Function(method) : default;
			return v.Exists;
		}

		private bool TryGetGlobalClass(string key, out ScriptVar v)
		{
			v = script.ReflectionsData.TryGetGlobalClass(key, out var type) ? ScriptVar.Class(type) : default;
			return v.Exists;
		}
	}
}
