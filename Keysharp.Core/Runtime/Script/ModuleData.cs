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

		internal static ModuleData GetOrCreate(System.Type moduleType) => cache.GetOrAdd(moduleType, static t => new ModuleData(t));
	}

	public sealed class ModuleVars
	{
		private readonly System.Type moduleType;

		internal ModuleVars(System.Type moduleType)
		{
			this.moduleType = moduleType ?? throw new System.ArgumentNullException(nameof(moduleType));
		}

		public bool HasVariable(object key) => Script.TheScript.Vars.HasVariable(moduleType, key.ToString());
		public object GetVariable(object key) => Script.TheScript.Vars.GetVariable(moduleType, key.ToString());
		public object SetVariable(object key, object value) => Script.TheScript.Vars.SetVariable(moduleType, key.ToString(), value);
	}
}
