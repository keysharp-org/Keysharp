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
		// Internal rather than private: Lowerer.CheckNamedArgs resolves a bare built-in class name to its type so
		// `#Warn NamedArg` can check a constructor call (`Buffer(nosuch: 1)`) against __New's parameter names.
		internal readonly Dictionary<string, Type> classTypesByName = new(StringComparer.OrdinalIgnoreCase);
        internal List<(string, bool)> preloadedDlls = [];
		internal DateTime startTime = DateTime.UtcNow;
		private readonly OrderedDictionary<Type, Dictionary<string, MethodPropertyHolder>> moduleVars;
		// Defensive fallback for a script with no modules at all (the generated program always has the main module,
		// so this is effectively never used).
		private Dictionary<string, MethodPropertyHolder> programVars;
		// The variable store for the module currently executing on this thread (the main module when none is active,
		// e.g. on the UI thread). Replaces the former standalone globalVars field, which was only ever an alias of
		// the main module's store.
		internal Dictionary<string, MethodPropertyHolder> GlobalVars => GetModuleVars(script.CurrentModuleType);
		// All modules and their global-variable stores, in declaration order, for ListVars.
		internal IEnumerable<KeyValuePair<Type, Dictionary<string, MethodPropertyHolder>>> AllModuleVars => moduleVars;
		private readonly Type defaultModuleType;
		internal Type DefaultModuleType => defaultModuleType;

		internal Variables(Script script)
		{
			this.script = script ?? throw new ArgumentNullException(nameof(script));
			moduleVars = GatherModuleVariables(script.ProgramType);
			if (moduleVars.Count == 0)
				return;
			defaultModuleType = moduleVars.Keys.FirstOrDefault(t => t.Name.Equals(Keywords.MainModuleName, StringComparison.OrdinalIgnoreCase)) ?? moduleVars.First().Key;
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
			// Public only. Everything the lowerer emits into a module class is `public static` (see ObjField),
			// including the SL_ static-local backing fields, so NonPublic could only ever reach members no script
			// wrote -- Program's own `MainScript`, and any private helper. It also made C# accessibility meaningless
			// inside a `#CSharp` block, where `private static long[] scratch` still became a script global.
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
					vars[name] = MethodPropertyHolder.GetOrAdd(field);
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
					vars[name] = MethodPropertyHolder.GetOrAdd(prop);
				}
			}

			return vars;
		}

		internal static bool IsSpecialName(string name) => name.Length > 3 && char.IsAsciiLetterUpper(name[0]) && char.IsAsciiLetterUpper(name[1]) && name[2] == '_';

		private static bool IsModuleType(Type type) =>
			typeof(Module).IsAssignableFrom(type);

		private static OrderedDictionary<Type, Dictionary<string, MethodPropertyHolder>> GatherModuleVariables(Type programType)
		{
			var map = new OrderedDictionary<Type, Dictionary<string, MethodPropertyHolder>>();
			if (programType == null)
				return map;

			var flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
			foreach (var nested in programType.GetNestedTypes(flags).Where(IsModuleType))
				map[nested] = GatherTypeVariables(nested);

			return map;
		}

		internal Dictionary<string, MethodPropertyHolder> GetModuleVars(Type moduleType)
		{
			// A null module means "the global scope": resolve to the main module's store. Only a script with no
			// modules at all (never in practice) has nothing to resolve to; gather from the program type then.
			moduleType ??= defaultModuleType;
			if (moduleType == null)
				return programVars ??= GatherTypeVariables(script.ProgramType);

			if (moduleVars.TryGetValue(moduleType, out var vars))
				return vars;

			vars = GatherTypeVariables(moduleType);
			moduleVars[moduleType] = vars;
			return vars;
		}

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
			CacheClassTypeNames(types);

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

		private void CacheClassTypeNames(IEnumerable<Type> types)
		{
			foreach (var type in types)
			{
				if (Struct.IsAutoPointerClass(type))
					continue;

				var name = Script.GetUserDeclaredName(type) ?? type.Name;

				if (!string.IsNullOrEmpty(name))
					classTypesByName[name] = type;
			}
		}

		private bool TryGetClassValue(string key, out object value)
		{
			value = null;
			if (!classTypesByName.TryGetValue(key, out var type))
				return false;

			if (Statics.TryGetValue(type, out var staticObj))
			{
				value = staticObj;
				return true;
			}

			return false;
		}

		public bool HasVariable(string key) => HasVariable(script.CurrentModuleType, key);

		public bool HasVariable(Type moduleType, string key)
		{
			var vars = GetModuleVars(moduleType);
			return vars.ContainsKey(key)
				|| script.ReflectionsData.flatPublicStaticProperties.ContainsKey(key)
				|| script.ReflectionsData.flatPublicStaticMethods.ContainsKey(key);
		}

		public object GetVariable(string key) => GetVariable(script.CurrentModuleType, key);

		public object GetVariable(Type moduleType, string key, bool exportsOnly = false)
		{
			var vars = GetModuleVars(moduleType);
			if (vars.TryGetValue(key, out var mph) && (!exportsOnly || mph.IsExported))
				return mph.CallFunc(null, null);

			var rv = GetReservedVariable(key);
			if (rv != null)
				return rv;

			if (TryGetClassValue(key, out var classValue))
				return classValue;

			return Functions.Func(key, moduleType);
		}

		public object SetVariable(string key, object value) => SetVariable(script.CurrentModuleType, key, value);

		public object SetVariable(Type moduleType, string key, object value)
		{
			var vars = GetModuleVars(moduleType);
			if (vars.TryGetValue(key, out var mph))
				SetMemberValue(mph, value);
			else
				_ = SetReservedVariable(key, value);

			return value;
		}

		private static void SetMemberValue(MethodPropertyHolder mph, object value)
		{
			if (mph.SetProp != null)
			{
				mph.SetProp(null, value);
				return;
			}

			if (mph.pi != null)
			{
				_ = Errors.PropertyErrorOccurred($"Property {mph.pi.Name} is read-only.");
				return;
			}

			if (mph.fi != null)
			{
				_ = Errors.PropertyErrorOccurred($"Field {mph.fi.Name} is read-only.");
			}
		}

		private PropertyInfo FindReservedVariable(string name)
		{
			_ = script.ReflectionsData.flatPublicStaticProperties.TryGetValue(name, out var prop);
			return prop;
		}

		private object GetReservedVariable(string name)
		{
			var prop = FindReservedVariable(name);
			return prop == null || !prop.CanRead ? null : prop.GetValue(null);
		}

		private bool SetReservedVariable(string name, object value)
		{
			var prop = FindReservedVariable(name);
			var set = prop != null && prop.CanWrite;

			if (set)
			{
				// The same policy typed parameters, properties and fields get, rather than the narrower one this
				// used to have of its own (Script.ForceType, which handled only long/double/string and passed
				// everything else straight to the reflection binder below to reject).
				value = ArgCoercer.CoerceValue(value, prop.PropertyType);
				prop.SetValue(null, value);
			}

			return set;
		}



		public object this[object key]
        {
			get => GetPropertyValueOrNull(key, "__Value") ?? GetVariable(key.ToString()) ?? "";
			set => _ = (key is KeysharpObject kso && Functions.HasProp(kso, "__Value") == 1) ? Script.SetPropertyValue(kso, "__Value", value) : SetVariable(key.ToString(), value);
		}
	}
}
