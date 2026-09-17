using Keysharp.Builtins;
using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;
using Keysharp.Internals.Cryptography;
using Keysharp.Internals.Invoke;

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
		private readonly Type[] programModules;
		// Defensive fallback for a script with no modules at all (the generated program always has the main module,
		// so this is effectively never used).
		private Dictionary<string, MethodPropertyHolder> programVars;
		// The variable store for the module currently executing on this thread (the main module when none is active,
		// e.g. on the UI thread).
		internal Dictionary<string, MethodPropertyHolder> GlobalVars => GetModuleVars(script.CurrentModuleType);
		// All modules and their global-variable stores, in declaration order, for ListVars.
		internal IEnumerable<KeyValuePair<Type, Dictionary<string, MethodPropertyHolder>>> AllModuleVars =>
			programModules.Select(type => KeyValuePair.Create(type, modules[type].Variables));
		private readonly Type defaultModuleType;
		internal Type DefaultModuleType => defaultModuleType;

		internal Variables(Script script)
		{
			this.script = script ?? throw new ArgumentNullException(nameof(script));
			var flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
			programModules = script.ProgramType?.GetNestedTypes(flags).Where(IsModuleType).ToArray() ?? [];

			foreach (var type in programModules)
				modules[type] = new(type);

			if (programModules.Length == 0)
				return;

			defaultModuleType = programModules.FirstOrDefault(t => t.Name.Equals(Keywords.MainModuleName, StringComparison.OrdinalIgnoreCase)) ?? programModules[0];
		}

		private sealed class ModuleScope
		{
			internal readonly Dictionary<string, MethodPropertyHolder> Variables;
			internal readonly Dictionary<string, Type> Classes;
			// The functions the module type itself declares, so a name which is none costs one lookup.
			internal readonly HashSet<string> Functions;
			internal readonly Type[] WildcardImports;
			internal readonly bool IsBuiltin;

			internal ModuleScope(Type type)
			{
				Variables = GatherTypeVariables(type);
				WildcardImports = type.GetCustomAttribute<WildcardImportAttribute>()?.Modules ?? [];
				IsBuiltin = type.Assembly == typeof(Module).Assembly;

				// A script module's functions and classes are among its variables, and its other methods are the compiler's own.
				if (IsBuiltin)
				{
					Functions = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
						.Where(m => !m.IsSpecialName).Select(m => Script.GetUserDeclaredName(m) ?? m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
					Classes = type.GetNestedTypes(BindingFlags.Public)
						.Where(t => !Struct.IsAutoPointerClass(t) && !IsModuleType(t) && typeof(Any).IsAssignableFrom(t))
						.ToDictionary(t => Script.GetUserDeclaredName(t) ?? t.Name, StringComparer.OrdinalIgnoreCase);
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
					if (field.GetCustomAttribute<PublicHiddenFromUser>() != null) continue;
					vars[Script.GetUserDeclaredName(field) ?? name] = MethodPropertyHolder.GetOrAdd(field);
				}
			}

			if (vartypes.HasFlag(VariableType.Property))
			{
				foreach (var prop in props)
				{
					var name = prop.Name;
					if (name.StartsWith("@", StringComparison.Ordinal)) name = name.Substring(1);
					bool isSpecial = IsSpecialName(name);
					if (wantspecial && !isSpecial) continue;
					if (wantnormal && isSpecial) continue;
					if (prop.GetCustomAttribute<PublicHiddenFromUser>() != null) continue;
					vars[Script.GetUserDeclaredName(prop) ?? name] = MethodPropertyHolder.GetOrAdd(prop);
				}
			}

			return vars;
		}

		internal static bool IsSpecialName(string name) => name.Length > 3 && char.IsAsciiLetterUpper(name[0]) && char.IsAsciiLetterUpper(name[1]) && name[2] == '_';

		private static bool IsModuleType(Type type) =>
			typeof(Module).IsAssignableFrom(type);

		internal Dictionary<string, MethodPropertyHolder> GetModuleVars(Type moduleType)
		{
			// A null module means "the global scope": resolve to the main module's store. Only a script with no
			// modules at all (never in practice) has nothing to resolve to; gather from the program type then.
			moduleType ??= defaultModuleType;
			if (moduleType == null)
				return programVars ??= GatherTypeVariables(script.ProgramType);

			return GetModule(moduleType).Variables;
		}

		private ModuleScope GetModule(Type type) => modules.GetOrAdd(type, static t => new(t));

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
			ksoStatic.DefinePropInternal("Prototype", new OwnPropsDesc(ksoStatic, Prototypes[typeof(KeysharpObject)]));
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

		// What a module-level write assigns: the module's own variable, else a built-in variable.
		internal bool TryGetModuleWriteTarget(Type module, string key, out ScriptVar v) =>
			TryGetModuleVar(module, key, out v) || TryGetBuiltinVar(key, out v);

		// What a module itself declares, imports aside: for a script module a variable (which includes a name it binds by
		// import), function or class, and for a built-in module a function, class or property, in the order the compiler
		// binds each.
		internal bool TryGetModuleVar(Type module, string key, out ScriptVar v)
		{
			v = default;

			if ((module ??= defaultModuleType) == null)
				return false;

			var scope = GetModule(module);

			if (!scope.IsBuiltin)
				v = scope.Variables.TryGetValue(key, out var variable) ? ScriptVar.Of(variable) : default;
			else if (scope.Functions.Contains(key) && Reflections.FindAndCacheStaticMethod(module, key, -1) is { } method
				&& Functions.MethodFunction(method.mi) is { } f)
				v = ScriptVar.Constant(f, f.Name);
			else if (scope.Classes.TryGetValue(key, out var type) && Statics.TryGetValue(type, out var cls))
				v = ScriptVar.Constant(cls, Script.GetUserDeclaredName(type) ?? type.Name);
			else if (scope.Variables.TryGetValue(key, out var property))
				v = ScriptVar.Of(property);

			return v.Exists;
		}

		// What the module's wildcard imports supply, the latest import first, which is no name starting with an underscore.
		private bool TryGetImported(Type module, string key, out ScriptVar v)
		{
			v = default;

			if ((module ??= defaultModuleType) == null || key.StartsWith('_'))
				return false;

			foreach (var imported in GetModule(module).WildcardImports)
				if (TryGetModuleVar(imported, key, out v))
					return true;

			return false;
		}

		// What a module object answers for: what the module declares, then what its wildcard imports supply, which as the
		// compiler's BuiltinWildcardSupplies rules is no name a built-in variable, function or class has.
		internal bool TryGetMember(Type module, string key, out ScriptVar v)
		{
			var rd = script.ReflectionsData;
			v = default;
			return TryGetModuleVar(module, key, out v)
				   || !rd.flatPublicStaticProperties.ContainsKey(key) && !rd.flatPublicStaticMethods.ContainsKey(key) && !rd.TryGetGlobalClass(key, out _)
				   && TryGetImported(module, key, out v);
		}

		internal bool TryGetBuiltinVar(string key, out ScriptVar v)
		{
			v = script.ReflectionsData.flatPublicStaticProperties.TryGetValue(key, out var prop) ? ScriptVar.Builtin(prop) : default;
			return v.Exists;
		}

		// What the AHK module holds: a built-in variable, else a built-in function or class.
		internal bool TryGetAhkMember(string key, out ScriptVar v) =>
			TryGetBuiltinVar(key, out v) || TryGetBuiltinFunction(key, out v) || TryGetGlobalClass(key, out v);

		// What a global name finds from a module: its own variable, a built-in, then a wildcard import, which a built-in
		// variable, function or class therefore wins over.
		internal bool TryGetGlobal(Type module, string key, out ScriptVar v) =>
			TryGetModuleVar(module, key, out v) || TryGetAhkMember(key, out v) || TryGetImported(module, key, out v);

		private bool TryGetBuiltinFunction(string key, out ScriptVar v)
		{
			v = script.ReflectionsData.flatPublicStaticMethods.TryGetValue(key, out var method) && Functions.MethodFunction(method) is { } f
				? ScriptVar.Constant(f, f.Name) : default;
			return v.Exists;
		}

		private bool TryGetGlobalClass(string key, out ScriptVar v)
		{
			v = script.ReflectionsData.TryGetGlobalClass(key, out var type) && Statics.TryGetValue(type, out var cls)
				? ScriptVar.Constant(cls, Script.GetUserDeclaredName(type) ?? type.Name) : default;
			return v.Exists;
		}
	}
}
