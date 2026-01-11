using System.Collections.Generic;
using System.Reflection;
using System.Xml.Linq;
using Keysharp.Core.Common.Cryptography;

namespace Keysharp.Scripting
{
	public class Variables
	{
        public LazyDictionary<Type, Any> Prototypes = new();
		public LazyDictionary<Type, Any> Statics = new();
        internal List<(string, bool)> preloadedDlls = [];
		internal DateTime startTime = DateTime.UtcNow;
		internal readonly Dictionary<string, MemberInfo> globalVars = new (StringComparer.OrdinalIgnoreCase);

		public Variables()
		{
			var flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
			var fields = TheScript.ProgramType.GetFields(flags);
			var props = TheScript.ProgramType.GetProperties(flags);

			// If ProgramType has a nested type called "UserDeclaredClasses", include its members too.
			var udc = TheScript.ProgramType.GetNestedType(Keywords.UserDeclaredClassesContainerName, flags);
			if (udc != null)
			{
				fields = fields.Concat(udc.GetFields(flags));
				props = props.Concat(udc.GetProperties(flags));
			}

			_ = globalVars.EnsureCapacity(fields.Length + props.Length);

			foreach (var field in fields)
				globalVars[field.Name] = field;

			foreach (var prop in props)
				globalVars[prop.Name] = prop;
		}

		public void InitClasses()
		{
			var anyType = typeof(Any);
			var script = Script.TheScript;
			var types = script.ReflectionsData.stringToTypes.Values
				.Where(type => type.IsClass && !type.IsAbstract && anyType.IsAssignableFrom(type));

			if (script.ProgramType != null)
			{
				var nested = Reflections.GetNestedTypes(script.ProgramType.GetNestedTypes()).Where(type => type.IsClass && anyType.IsAssignableFrom(type));
				types = types.Concat(nested);
			}

			/*
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.IsClass && !type.IsAbstract && anyType.IsAssignableFrom(type));
			*/

			// Initiate necessary base types in specific order
			InitClass(typeof(FuncObj));
			// Need to do this so that FuncObj methods contain themselves in the prototype,
			// meaning a circular reference. This shouldn't prevent garbage collection, but
			// I haven't verified that.
			var fop = Prototypes[typeof(FuncObj)];
			foreach (var op in fop.op)
			{
				var opm = op.Value;
				if (opm.Value is FuncObj fov && fov != null)
				{
					fov.SetBaseInternal(fop);
				}
				if (opm.Get is FuncObj fog && fog != null)
				{
					fog.SetBaseInternal(fop);
				}
				if (opm.Set is FuncObj fos && fos != null)
				{
					fos.SetBaseInternal(fop);
				}
				if (opm.Call is FuncObj foc && foc != null)
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
			ksoStatic.DefinePropInternal("prototype", new OwnPropsDesc(ksoStatic, Prototypes[typeof(KeysharpObject)]));
			// Object.Base == Any
			ksoStatic.SetBaseInternal(Statics[typeof(Any)]);

			//FuncObj was initialized when Object wasn't, so define the bases
			Prototypes[typeof(FuncObj)].SetBaseInternal(Prototypes[typeof(KeysharpObject)]);
			Statics[typeof(FuncObj)].SetBaseInternal(Statics[typeof(Class)]);

			// Do not initialize the core types again
			var typesToRemoveSet = new HashSet<Type>(new[] { typeof(Any), typeof(FuncObj), typeof(KeysharpObject), typeof(Class) });
			var orderedTypes = types.Where(type => !typesToRemoveSet.Contains(type)).OrderBy(Reflections.GetInheritanceDepth);

			// Lazy-initialize all other classes
			foreach (var t in orderedTypes)
			{
				Script.InitClass(t);
			}
		}

		public bool HasVariable(string key) =>
			globalVars.ContainsKey(key)
			|| Script.TheScript.ReflectionsData.flatPublicStaticProperties.ContainsKey(key)
			|| Script.TheScript.ReflectionsData.flatPublicStaticMethods.ContainsKey(key);

        public object GetVariable(string key)
		{
			if (globalVars.TryGetValue(key, out var field))
			{
				if (field is PropertyInfo pi)
					return pi.GetValue(null);
				else if (field is FieldInfo fi)
					return fi.GetValue(null);
			}

			var rv = GetReservedVariable(key); // Try reserved variable first, to take precedence over IFuncObj
			if (rv != null)
				return rv;

			return Functions.GetFuncObj(key, null);

        }

		public object SetVariable(string key, object value)
		{
			if (globalVars.TryGetValue(key, out var field))
			{
				if (field is PropertyInfo pi)
					pi.SetValue(null, value);
				else if (field is FieldInfo fi)
					fi.SetValue(null, value);
			}
			else
				_ = SetReservedVariable(key, value);

			return value;
		}

		private PropertyInfo FindReservedVariable(string name)
		{
			_ = Script.TheScript.ReflectionsData.flatPublicStaticProperties.TryGetValue(name, out var prop);
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
				value = Script.ForceType(prop.PropertyType, value);
				prop.SetValue(null, value);
			}

			return set;
		}



		public object this[object key]
        {
			get => TryGetPropertyValue(out object val, key, "__Value") ? val : GetVariable(key.ToString()) ?? "";
			set => _ = (key is KeysharpObject kso && Functions.HasProp(kso, "__Value") == 1) ? Script.SetPropertyValue(kso, "__Value", value) : SetVariable(key.ToString(), value);
		}

		public class Dereference
		{
            private readonly Dictionary<string, object> vars = new(StringComparer.OrdinalIgnoreCase);
			private eScope scope = eScope.Local;
			private HashSet<string> globals;
            public Dereference(eScope funcScope, string[] funcGlobals, params object[] args)
			{
				scope = funcScope;
				globals = funcGlobals == null ? null : new HashSet<string>(funcGlobals, StringComparer.OrdinalIgnoreCase);

				for (int i = 0; i < args.Length; i += 2)
				{
					if (args[i] is string varName)
					{
						vars[varName] = args[i + 1];
					}
				}
			}

            public object this[object key]
			{
				get
				{
					if (key is KeysharpObject)
						return GetPropertyValue(key, "__Value");
					if (vars.TryGetValue(key.ToString(), out var val))
						return GetPropertyValue(val, "__Value");
					return Script.TheScript.Vars[key];
				}
				set
				{
					if (key is KeysharpObject)
					{
						SetPropertyValue(key, "__Value", value);
						return;
					}

					var s = key.ToString();
					if (vars.TryGetValue(s, out var val))
					{
						SetPropertyValue(val, "__Value", value);
						return;
					}
					if ((scope == eScope.Global || (globals?.Contains(s) ?? false)) && Script.TheScript.Vars.HasVariable(s))
					{
						Script.TheScript.Vars[s] = value;
						return;
					}

					vars[s] = new VarRef(null);
                }
			}
        }
		}
}