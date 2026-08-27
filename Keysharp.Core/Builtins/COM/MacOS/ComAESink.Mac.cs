#if OSX
using Keysharp.Internals.AppleEvents;
using Keysharp.Internals.Invoke;

namespace Keysharp.Builtins.COM
{
	/// <summary>
	/// A live ComObjConnect subscription. Distributed notifications published under the application's bundle
	/// identifier are matched and delivered on a Keysharp pseudo-thread exactly like a COM event sink. Handlers
	/// receive the notification's payload followed by the ComObject itself, matching the sink contract on the
	/// other platforms.
	/// </summary>
	internal sealed class ComAESink : IDisposable
	{
		private readonly Script script;
		private readonly ComObject target;
		private readonly string prefix;
		private readonly KeysharpObject sinkObj;
		private readonly Dictionary<string, MethodPropertyHolder> methodMapper = new (StringComparer.OrdinalIgnoreCase);
		private IDisposable subscription;
		private bool disposed;

		internal ComAESink(ComObject target, object sinkOrPrefix)
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
			var bundleId = target.BundleId;

			if (string.IsNullOrEmpty(bundleId))
			{
				_ = Errors.ErrorOccurred("ComObjConnect needs an application addressed by bundle id; a process id publishes no identifiable notifications.");
				return;
			}

			try
			{
				// An application's notifications are conventionally named after its bundle id, which is the only
				// handle available: nothing describes what an application publishes ahead of time. The convention
				// is not a rule — Music still posts under com.apple.iTunes — so an application that renamed its
				// bundle publishes names this will not match.
				subscription = AENotifications.Subscribe(bundleId, Deliver);
			}
			catch (Exception ex)
			{
				_ = Errors.ErrorOccurred($"Could not subscribe to notifications from '{bundleId}': {ex.Message}");
			}
		}

		/// <summary>
		/// Runs on the notification run loop; hands the callback to the Keysharp thread and returns at once.
		/// Nothing may throw from here: the caller is Core Foundation.
		/// </summary>
		private void Deliver(string notification, object payload)
		{
			if (disposed || script.IsDisposed || script.hasExited)
				return;

			// "com.apple.Music.playerInfo" on com.apple.Music invokes Prefix_playerInfo.
			var member = notification.Length > target.BundleId.Length && notification[target.BundleId.Length] == '.'
						 ? notification[(target.BundleId.Length + 1)..]
						 : notification;
			var args = new object[] { notification, payload, target };

			if (prefix != null)
			{
				if (!methodMapper.TryGetValue(member, out var mph))
					return;

				script.Threads.LaunchThreadInMain(
					() => _ = Keysharp.Internals.Flow.TryCatch(() => mph.CallFunc(null, args)),
					kind: ThreadKind.Com);
			}
			else if (sinkObj != null)
			{
				var (_, found) = Script.GetMethodOrProperty(sinkObj, member, -1, checkBase: true, throwIfMissing: false, invokeMeta: true);

				if (found == null)
					return;

				script.Threads.LaunchThreadInMain(
					() => _ = Keysharp.Internals.Flow.TryCatch(() => Script.Invoke(sinkObj, member, args)),
					kind: ThreadKind.Com);
			}
		}

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;

			try
			{
				subscription?.Dispose();
			}
			catch
			{
			}

			subscription = null;
		}
	}
}
#endif
