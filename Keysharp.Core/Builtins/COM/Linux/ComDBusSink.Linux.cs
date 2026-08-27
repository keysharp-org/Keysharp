#if LINUX
using Keysharp.Internals.DBus;
using Keysharp.Internals.Invoke;

namespace Keysharp.Builtins.COM
{
	/// <summary>
	/// A live ComObjConnect subscription: every signal the object publishes is matched, and delivery is marshalled
	/// onto a Keysharp pseudo-thread exactly like a COM event sink. Handlers receive the signal's arguments
	/// followed by the ComObject itself, matching the Windows sink contract.
	/// </summary>
	internal sealed class ComDBusSink : IDisposable
	{
		private readonly Script script;
		private readonly ComObject target;
		private readonly string prefix;
		private readonly KeysharpObject sinkObj;
		private readonly Dictionary<string, MethodPropertyHolder> methodMapper = new (StringComparer.OrdinalIgnoreCase);
		private readonly List<IDisposable> subscriptions = [];
		private bool disposed;

		internal ComDBusSink(ComObject target, object sinkOrPrefix)
		{
			this.target = target;
			script = Script.TheScript;

			if (sinkOrPrefix is string s)
			{
				prefix = s;

				if (!script.ReflectionsData.typeToStringStaticMethods.ContainsKey(script.CurrentModuleType))
					Reflections.FindAndCacheMethod(script.CurrentModuleType, "", -1);

				foreach (var kv in script.ReflectionsData.typeToStringStaticMethods[script.CurrentModuleType])
				{
					if (string.Equals(kv.Key, Keysharp.Language.Keywords.AutoExecSectionName, StringComparison.OrdinalIgnoreCase))
						continue;

					if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
						methodMapper[kv.Key.Remove(0, prefix.Length)] = kv.Value.First().Value;
				}
			}
			else if (sinkOrPrefix is KeysharpObject ko)
				sinkObj = ko;
			else
			{
				_ = Errors.ValueErrorOccurred($"ComObjConnect needs a function-name prefix or an object, got '{sinkOrPrefix?.GetType().Name}'.");
				return;
			}

			Subscribe();
		}

		private void Subscribe()
		{
			// Bind to the current owner so a service restart cannot silently feed us another process's signals.
			var owner = DBusCalls.GetNameOwner(target.bus, target.service);

			foreach (var (iface, signal) in target.AllSignals())
			{
				var name = signal.Name;

				try
				{
					subscriptions.Add(DBusCalls.WatchSignal(
										  target.bus, owner, target.path, iface.Name, name, signal.Signature,
										  args => Deliver(name, args)));
				}
				catch (Exception ex)
				{
					_ = Diagnostics.Debug.WriteLine($"Could not subscribe to {iface.Name}.{name}: {ex.Message}");
				}
			}
		}

		/// <summary>Runs on a Tmds dispatch thread; hands the callback to the Keysharp thread and returns at once.</summary>
		private void Deliver(string signalName, object[] args)
		{
			if (disposed || script.IsDisposed || script.hasExited)
				return;

			var allArgs = new object[(args?.Length ?? 0) + 1];

			if (args != null)
				System.Array.Copy(args, allArgs, args.Length);

			allArgs[^1] = target;

			if (prefix != null)
			{
				if (!methodMapper.TryGetValue(signalName, out var mph))
					return;

				script.Threads.LaunchThreadInMain(
					() => _ = Keysharp.Internals.Flow.TryCatch(() => mph.CallFunc(null, allArgs)),
					kind: ThreadKind.Com);
			}
			else if (sinkObj != null)
			{
				var (_, found) = Script.GetMethodOrProperty(sinkObj, signalName, -1, checkBase: true, throwIfMissing: false, invokeMeta: true);

				if (found == null)
					return;

				script.Threads.LaunchThreadInMain(
					() => _ = Keysharp.Internals.Flow.TryCatch(() => Script.Invoke(sinkObj, signalName, allArgs)),
					kind: ThreadKind.Com);
			}
		}

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;

			foreach (var s in subscriptions)
			{
				try { s.Dispose(); } catch { }
			}

			subscriptions.Clear();
		}
	}
}
#endif
