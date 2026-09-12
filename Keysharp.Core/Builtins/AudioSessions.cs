using Engine = Keysharp.Internals.Audio;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		public partial class Audio
		{
			/// <summary>Whether this host can enumerate and control other applications' audio.</summary>
			public static object staticget_IsSessionControlSupported(object @this)
				=> Service.Supports(Engine.AudioCapability.Sessions);

			/// <summary>Whether this host can observe a device or session level through an explicit meter.</summary>
			public static object staticget_IsMeteringSupported(object @this)
				=> Service.Supports(Engine.AudioCapability.Metering);

			/// <summary>Whether this host can capture from a microphone or other input device.</summary>
			public static object staticget_IsMicrophoneCaptureSupported(object @this)
				=> Service.Supports(Engine.AudioCapability.MicrophoneCapture);

			/// <summary>Whether this host can capture what an output device is playing.</summary>
			public static object staticget_IsSystemAudioCaptureSupported(object @this)
				=> Service.Supports(Engine.AudioCapability.SystemAudioCapture);

			/// <summary>
			/// Every live application audio session, optionally narrowed to one process and one device. A process
			/// routinely owns several sessions, so every match is returned rather than the first.
			/// </summary>
			[Static]
			public static object Sessions(object @this, [UserDeclaredName("PIDOrName")] object pidOrName = null, object device = null)
			{
				if (!TryValidateSelector(pidOrName, out var selectorFailure))
					return selectorFailure;

				var result = new Array();
				var backend = Service.Backend;

				if (!Service.Supports(Engine.AudioCapability.Sessions))
					return result;

				var deviceId = "";

				if (device is Ks.Audio.Device || device.As().Length > 0)
				{
					if (!TryResolveDeviceAnyKind(device, out var resolved, out var failure))
						return failure;

					deviceId = resolved.Id;
				}

				foreach (var s in backend.EnumerateSessions(deviceId))
					if (Matches(s, pidOrName))
						_ = result.Push(Session.Wrap(Service, s));

				return result;
			}

			/// <summary>
			/// Sets one absolute volume on every session of a process. Returns how many were changed, which is
			/// zero when nothing matched; a per-session failure aggregates into one actionable error afterwards.
			/// </summary>
			[Static]
			public static object SetApplicationVolume(object @this, [UserDeclaredName("PIDOrName")] object pidOrName, object volume, object device = null)
				=> ApplyToSessions(pidOrName, device, volume, true);

			/// <summary>Sets one absolute mute state on every session of a process, and returns how many changed.</summary>
			[Static]
			public static object SetApplicationMute(object @this, [UserDeclaredName("PIDOrName")] object pidOrName, object mute, object device = null)
				=> ApplyToSessions(pidOrName, device, mute, false);

			private static object ApplyToSessions(object pidOrName, object device, object value, bool isVolume)
			{
				if (!TryValidateSelector(pidOrName, out var selectorFailure))
					return selectorFailure;

				var backend = Service.Backend;

				if (!Service.Supports(Engine.AudioCapability.Sessions))
					return Errors.OSErrorOccurredWithMessage($"Application audio control is unavailable: {Service.UnsupportedReason(Engine.AudioCapability.Sessions)}");

				var scalar = 0f;

				if (isVolume && !TryLevel(value, "Volume", out scalar, out var failure))
					return failure;

				var deviceId = "";

				if (device is Ks.Audio.Device || device.As().Length > 0)
				{
					if (!TryResolveDeviceAnyKind(device, out var resolved, out var deviceFailure))
						return deviceFailure;

					deviceId = resolved.Id;
				}

				var mute = !isVolume && value.Ab();
				var matched = 0;
				var changed = 0;
				var expired = 0;
				string firstFailure = null;

				foreach (var s in backend.EnumerateSessions(deviceId))
				{
					if (!Matches(s, pidOrName))
						continue;

					matched++;
					var ok = isVolume ? backend.TrySetSessionVolume(s.Id, scalar) : backend.TrySetSessionMute(s.Id, mute);

					if (ok)
						changed++;
					else if (!backend.TryRefreshSession(s.Id, out _))
						expired++;                       // ordinary disappearance between the snapshot and the write
					else
						firstFailure ??= $"session {s.Id}";
				}

				if (firstFailure != null)
					return Errors.OSErrorOccurredWithMessage(
							   $"Matched {matched} sessions, changed {changed}, {expired} expired, and could not change the rest; the first was {firstFailure}.");

				return (long)changed;
			}

			/// <summary>
			/// Validates the process selector once, before anything is enumerated. Checking it per session would
			/// make a malformed argument depend on how many sessions happen to exist, so a host with none would
			/// silently accept it.
			/// </summary>
			internal static bool TryValidateSelector(object pidOrName, out object failure)
			{
				failure = null;
				var text = pidOrName.As();

				if (text.Contains('\\') || text.Contains('/'))
				{
					failure = Errors.ValueErrorOccurred("PIDOrName is a process id or an executable name, not a path.", text);
					return false;
				}

				return true;
			}

			private static bool Matches(in Engine.AudioSessionDescriptor s, object pidOrName)
			{
				var text = pidOrName.As();

				if (text.Length == 0)
					return true;

				if (long.TryParse(text, out var pid) && s.ProcessId == pid)
					return true;

				var name = s.ProcessName ?? "";

				if (name.Length == 0)
					return false;

				return string.Equals(name, text, StringComparison.OrdinalIgnoreCase)
					   || string.Equals(name, text + ".exe", StringComparison.OrdinalIgnoreCase)
					   || string.Equals(name + ".exe", text, StringComparison.OrdinalIgnoreCase);
			}

			/// <summary>
			/// Subscribes to device arrivals, removals, renames and default changes. The callback receives
			/// <c>(hook, kind, device)</c>, where kind is "Added", "Removed", "Changed" or "DefaultChanged". The
			/// subscription is rooted until <c>Stop()</c> or script teardown, so dropping the
			/// returned hook does not unsubscribe it. The Kind filter is "Output", "Input" or "All" (the default),
			/// matched without regard to case.
			/// </summary>
			[Static]
			public static object OnDeviceChange(object @this, object callback, object kind = null)
			{
				var fo = Functions.GetKeysharpFunc(callback, null, true);

				if (fo == null)
					return Errors.TypeErrorOccurred(callback, typeof(KeysharpFunc));

				if (!TryKind(kind, "All", true, out var deviceKind, out var all, out var failure))
					return failure;

				if (!Service.Supports(Engine.AudioCapability.DeviceChange))
					return Unsupported(Engine.AudioCapability.DeviceChange, "Audio.OnDeviceChange");

				var script = Script.TheScript;
				var manager = script.AudioEventManager;
				var reg = new Engine.AudioEventRegistration(fo, script.EventScheduler, manager,
													   all ? "All" : KindName(deviceKind));
				var hook = new DeviceHook { sub = reg };
				reg.scriptObject = hook;
				manager.Register(reg);
				return hook;
			}

			/// <summary>
			/// The handle <c>Audio.OnDeviceChange</c> returns. Its whole surface comes from the shared
			/// <c>Ks.EventHook</c>, so audio adds no second spelling of a contract every other event source already has.
			/// </summary>
			public sealed class DeviceHook : EventHook
			{
				internal DeviceHook() : base() { }
			}

			/// <summary>Every live <c>Audio.OnDeviceChange</c> subscription, oldest first (script: <c>Audio.Hooks</c>) —
			/// a snapshot to iterate and drop. Device-change hooks only, never voices or meters.</summary>
			public static object staticget_Hooks(object @this) => Script.TheScript.AudioEventManager.Hooks();

			/// <summary>A device selector that accepts either kind, used where a session may live on either.</summary>
			internal static bool TryResolveDeviceAnyKind(object selector, out Engine.AudioDeviceDescriptor device, out object failure)
			{
				if (TryResolveDevice(selector, Engine.AudioDeviceKind.Output, true, out device, out var outputFailure))
				{
					failure = null;
					return true;
				}

				if (TryResolveDevice(selector, Engine.AudioDeviceKind.Input, true, out device, out var inputFailure))
				{
					failure = null;
					return true;
				}

				// Reporting only the input attempt would tell a script that named an output that no INPUT device
				// matched, which points at the wrong thing entirely.
				failure = Errors.OSErrorOccurredWithMessage($"No audio device matches that selector. As an output: {Describe(outputFailure)} As an input: {Describe(inputFailure)}");
				return false;
			}

			private static string Describe(object failure) => failure is Error e ? e.Message : "no match.";

			/// <summary>
			/// One application's audio on one device. The snapshot is generation-bound: it never rebinds to a
			/// different session, so a handle kept across an application restart expires instead of quietly
			/// controlling the newcomer.
			/// </summary>
			public class Session : KeysharpObject
			{
				internal Engine.AudioSessionDescriptor Snapshot;
				internal Engine.AudioService service;
				internal bool expired;

				public Session(params object[] args) : base(args) { }

				public override object __New(params object[] args)
					=> Errors.ErrorOccurred("Audio.Session is produced by Audio.Sessions(), not constructed.");

				internal static Session Wrap(Engine.AudioService owner, Engine.AudioSessionDescriptor s)
					=> new (null) { service = owner, Snapshot = s };

				public string Id => Snapshot.Id ?? "";

				/// <summary>The device this session plays or records on.</summary>
				public object Device
				{
					get
					{
						var backend = service?.Backend;
						return backend is { IsAvailable: true } && backend.TryGetDevice(Snapshot.DeviceId, out var d)
							   ? Ks.Audio.Device.Wrap(service, d)
							   : "";
					}
				}

				public object ProcessId => Snapshot.ProcessId > 0 ? Snapshot.ProcessId : (object)"";

				public object ProcessName => Snapshot.ProcessName.IsNullOrEmpty() ? "" : Snapshot.ProcessName;

				public object DisplayName => Snapshot.DisplayName.IsNullOrEmpty() ? "" : Snapshot.DisplayName;

				/// <summary>"Active", "Inactive" or "Expired".</summary>
				public string Status => expired ? "Expired" : Snapshot.State ?? "Inactive";

				public object IsSystemSounds => Snapshot.IsSystemSounds;

				public object IsVolumeSupported => Snapshot.HasVolume;

				public object IsMuteSupported => Snapshot.HasMute;

				public object IsMeteringSupported => Snapshot.HasMetering;

				public object Volume
				{
					get
					{
						var backend = Require(out var failure);

						if (backend == null)
							return failure;

						return backend.TryGetSessionVolume(Snapshot.Id, out var linear)
							   ? Level(Math.Clamp(linear, 0.0, 1.0))
							   : Errors.OSErrorOccurredWithMessage($"Cannot read the volume of session {Id}.");
					}

					set
					{
						var backend = Require(out _);

						if (backend == null || !TryLevel(value, "Volume", out var scalar, out _))
							return;

						if (!backend.TrySetSessionVolume(Snapshot.Id, scalar))
							_ = Errors.OSErrorOccurredWithMessage($"Cannot set the volume of session {Id}.");
					}
				}

				public object Mute
				{
					get
					{
						var backend = Require(out var failure);

						if (backend == null)
							return failure;

						return backend.TryGetSessionMute(Snapshot.Id, out var mute)
							   ? mute
							   : Errors.OSErrorOccurredWithMessage($"Cannot read the mute state of session {Id}.");
					}

					set
					{
						var backend = Require(out _);

						if (backend == null)
							return;

						if (!backend.TrySetSessionMute(Snapshot.Id, value.Ab()))
							_ = Errors.OSErrorOccurredWithMessage($"Cannot set the mute state of session {Id}.");
					}
				}

				/// <summary>Re-reads this exact session, or marks it Expired and returns blank once it is gone.</summary>
				public object Refresh()
				{
					var backend = service?.Backend;

					if (service.Supports(Engine.AudioCapability.Sessions) && backend.TryRefreshSession(Snapshot.Id, out var fresh))
					{
						Snapshot = fresh;
						expired = false;
						return this;
					}

					expired = true;
					return "";
				}

				/// <summary>The platform's own session object, with the same unspecified-type contract as Device.ToClr.</summary>
				public object ToClr()
				{
					var backend = Require(out var failure);

					if (backend == null)
						return failure;

					var native = backend.GetNativeSessionObject(Snapshot.Id);

					return native == null
						   ? Errors.OSErrorOccurredWithMessage("This platform exposes no native object for an audio session.")
						   : ManagedInvoke.WrapManaged(native);
				}

				private Engine.IAudioBackend Require(out object failure)
				{
					failure = null;

					if (expired)
					{
						failure = Errors.OSErrorOccurredWithMessage($"Session {Id} has expired; call Refresh() first.");
						return null;
					}

					var backend = service?.Backend;

					if (backend == null || !service.Supports(Engine.AudioCapability.Sessions))
					{
						failure = Errors.OSErrorOccurredWithMessage($"Application audio control is unavailable: {service?.UnsupportedReason(Engine.AudioCapability.Sessions) ?? "this platform has no per-application audio model."}");
						return null;
					}

					return backend;
				}

				public override string ToString() => ProcessName.As().Length > 0 ? ProcessName.As() : Id;
			}

			/// <summary>
			/// An explicit observation of a device or session level. Metering a target whose frames do not pass
			/// through Keysharp costs a live native stream on some backends, so it is a resource with a lifetime
			/// rather than a property that quietly opens one.
			/// </summary>
			public class Meter : KeysharpObject, IDisposable
			{
				/// <summary>Lets the owning service release a meter a script dropped without stopping.</summary>
				void IDisposable.Dispose() => _ = Dispose();

				internal Engine.AudioService service;
				internal Engine.IAudioNativeMeter native;
				internal string targetId = "";
				internal bool isSession;
				internal object target = "";
				internal string status = "Stopped";
				internal string error;

				public Meter(params object[] args) : base(args) { }

				/// <summary>
				/// Snapshots a target without opening anything. A blank target takes the current default output,
				/// which makes <c>Audio.Meter()</c> the useful minimal form.
				/// </summary>
				public object __New(object target = null, object intervalMilliseconds = null)
				{
					service = Service;
					var interval = intervalMilliseconds == null ? 50L : intervalMilliseconds.Al();

					if (interval < 20 || interval > 1000)
						return Errors.ValueErrorOccurred("IntervalMilliseconds must be from 20 through 1000.", intervalMilliseconds);

					Interval = interval;

					if (target is Session s)
					{
						this.target = s;
						targetId = s.Id;
						isSession = true;
						return DefaultObject;
					}

					if (target is Ks.Audio.Device || target.As().Length > 0)
					{
						if (!TryResolveDeviceAnyKind(target, out var d, out var failure))
							return failure;

						this.target = Ks.Audio.Device.Wrap(service, d);
						targetId = d.Id;
						return DefaultObject;
					}

					var backend = service.Backend;

					if (backend is { IsAvailable: true } && backend.TryGetDefaultDevice(Engine.AudioDeviceKind.Output, out var def))
					{
						this.target = Ks.Audio.Device.Wrap(service, def);
						targetId = def.Id;
					}

					return DefaultObject;
				}

				internal long Interval { get; private set; } = 50;

				public object Target => target;

				/// <summary>"Stopped", "Active", "Error" or "Disposed".</summary>
				public string Status => status;

				public object IntervalMilliseconds => Interval;

				/// <summary>Whether this target can be metered at all. False while no target resolved.</summary>
				public object IsSupported
					=> targetId.Length > 0 && service?.Supports(Engine.AudioCapability.Metering) == true;

				/// <summary>The most recent peak from 0 through 100, or blank before any observation completed.</summary>
				public object Peak
				{
					get
					{
						var m = native;

						if (m == null || status != "Active")
							return "";

						var p = m.Peak;
						return p < 0 ? "" : Level(Math.Clamp(p, 0.0, 1.0));
					}
				}

				public object Error => error.IsNullOrEmpty() ? "" : new Error(error);

				/// <summary>Opens the native observation. Raises for an unsupported, missing or expired target.</summary>
				public object Start()
				{
					if (status == "Disposed")
						return Errors.ValueErrorOccurred("This Audio.Meter has been disposed.");

					if (status == "Active")
						return this;

					var backend = service?.Backend;

					if (backend == null || !service.Supports(Engine.AudioCapability.Metering))
						return Errors.OSErrorOccurredWithMessage($"Metering is unavailable: {service?.UnsupportedReason(Engine.AudioCapability.Metering) ?? "this platform exposes no level observation."}");

					if (targetId.Length == 0)
						return Errors.OSErrorOccurredWithMessage("This meter has no target; there is no default output device.");

					if (!service.TryRegisterMeter(this))
						return Errors.OSErrorOccurredWithMessage($"This script already has {Engine.AudioService.MaxOpenMeters} meters running. Stop one before starting another.");

					if (!backend.TryOpenMeter(targetId, isSession, Interval, out var opened, out var openError))
					{
						service.UnregisterMeter(this);
						status = "Error";
						error = openError;
						return Errors.OSErrorOccurredWithMessage($"Cannot meter that target: {openError}");
					}

					native = opened;
					status = "Active";
					error = null;
					return this;
				}

				/// <summary>Reversible: releases the observation but keeps the target.</summary>
				public object Stop()
				{
					var m = native;
					native = null;

					try
					{
						m?.Dispose();
					}
					catch (Exception ex)
					{
						Diagnostics.Debug.WriteLine($"Audio meter stop failed: {ex.Message}");
					}

					service?.UnregisterMeter(this);

					if (status != "Disposed")
						status = "Stopped";

					return this;
				}

				/// <summary>Releases the observation when a script drops the meter without stopping it.</summary>
				public override object __Delete()
				{
					_ = Dispose();
					return base.__Delete();
				}

				public object Dispose()
				{
					_ = Stop();
					status = "Disposed";
					return DefaultObject;
				}

				public override string ToString() => "Audio.Meter";
			}
		}
	}
}
