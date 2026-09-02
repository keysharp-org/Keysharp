using Keysharp.Builtins;
using System.Collections.Concurrent;
using System.Reflection;

namespace Keysharp.Runtime
{
	public sealed class ModuleData
	{
		private static readonly ConcurrentDictionary<System.Type, ModuleData> cache = new ();

		internal System.Type ModuleType { get; }
		internal readonly Semver.SemVersion CompatibilityVersion;
		public ModuleVars Vars { get; }

		public ModuleData(System.Type moduleType)
		{
			ModuleType = moduleType ?? throw new System.ArgumentNullException(nameof(moduleType));
			CompatibilityVersion = ModuleType.GetCustomAttribute<CompatibilityModeAttribute>()?.Version;
			Vars = new ModuleVars(ModuleType);
		}

		internal static ModuleData GetOrCreate(System.Type moduleType)
		{
			if (moduleType == null)
				return null;

			return cache.GetOrAdd(moduleType, static t => new ModuleData(t));
		}

		internal ModuleData Push(System.Type moduleType, out bool changed)
		{
			changed = false;
			var script = Script.TheScript;
			var prev = script.moduleData.Value;

			if (moduleType == null || prev.ModuleType == moduleType || !typeof(Keysharp.Runtime.Module).IsAssignableFrom(moduleType))
				return prev;

			var next = GetOrCreate(moduleType);
			if (!ReferenceEquals(prev, next))
			{
				script.moduleData.Value = next;
				script.SetCurrentCompatibilityVersion(next.CompatibilityVersion);
				changed = true;
			}

			return prev;
		}

		internal void Pop(ModuleData previous, bool changed)
		{
			if (changed)
			{
				Script.TheScript.moduleData.Value = previous;
				Script.TheScript.SetCurrentCompatibilityVersion(previous?.CompatibilityVersion);
			}
		}
	}

	public sealed class ModuleVars
	{
		private readonly System.Type moduleType;

		internal ModuleVars(System.Type moduleType)
		{
			this.moduleType = moduleType ?? throw new System.ArgumentNullException(nameof(moduleType));
		}

		public object this[object key]
		{
			// The key is either a variable NAME or a reference standing in for one, so the reference test has to be
			// the provable one: anything else is a name, and a value that merely might answer to __Value would be
			// read as a reference here and never resolve as the name it is.
			get => (Refs.DeclaresValue(key) ? Refs.GetValueOrNull(key) : null) ?? Script.TheScript.Vars.GetVariable(moduleType, key.ToString()) ?? "";
			set => _ = Refs.DeclaresValue(key)
				? Refs.SetValue(key, value)
				: Script.TheScript.Vars.SetVariable(moduleType, key.ToString(), value);
		}

		public bool HasVariable(object key) => Script.TheScript.Vars.HasVariable(moduleType, key.ToString());
		public object GetVariable(object key) => Script.TheScript.Vars.GetVariable(moduleType, key.ToString());
		public object SetVariable(object key, object value) => Script.TheScript.Vars.SetVariable(moduleType, key.ToString(), value);
	}
}
