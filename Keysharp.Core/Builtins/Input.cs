using Keysharp.Internals;
using Keysharp.Internals.Input;

namespace Keysharp.Builtins
{
	public partial class Ks
	{
		public class Input : KeysharpObject
		{
			private readonly InputDeviceInfo device;
			private readonly Script owner;

			public Input() { }
			internal Input(InputDeviceInfo device, Script owner = null)
			{
				this.device = device;
				this.owner = owner ?? Script.TheScript;
			}

			public override object __New(params object[] Arguments)
				=> Errors.ErrorOccurred("Input instances cannot be constructed directly.");

			public object Id => device.Id != 0 ? (object)device.Id : DefaultObject;
			public string Kind => device.Kind;
			public string Name => Text(device.Name);
			public object VendorId => device.VendorId is uint value ? (object)(long)value : DefaultObject;
			public object ProductId => device.ProductId is uint value ? (object)(long)value : DefaultObject;
			public string HardwareId => Text(device.HardwareId);
			public string Manufacturer => Text(device.Manufacturer);
			public string Connection => Text(device.Connection);
			public string Location => Text(device.Location);
			public object IsInternal => device.IsInternal is bool value ? (object)value : DefaultObject;

			private static string Text(string value) => string.IsNullOrEmpty(value) ? DefaultObject : value;

			[Static]
			public static object OnDeviceChange(object @this, object Callback, object AddRemove = null)
			{
				if (!Functions.TryAddRemove(AddRemove, out var mode) || Functions.ToCallback(Callback) is not { } callback
						|| mode != 0 && !Functions.ValidateFunctor(callback, 2))
					return DefaultObject;

				var script = Script.TheScript;
				return script.InputEventManager.Modify(new CallbackRegistration(callback, script.EventScheduler, true, false), mode)
					? DefaultObject : Errors.OSErrorOccurredWithMessage("Input.OnDeviceChange notifications are unavailable.");
			}

			/// <summary>A snapshot of the current real thread's device-change callbacks, in invocation order.</summary>
			public static object staticget_Callbacks(object @this) => Script.TheScript.InputEventManager.Callbacks();

			public object GetKeyState(object KeyName)
			{
				if (!KeyName.CoerceString(out var name))
					return DefaultObject;

				if (owner.IsDisposed)
					return Errors.OSErrorOccurredWithMessage("The input device's script has ended.");

				uint? modifiers = null;
				var virtualKey = owner.HookThread.TextToVK(name, ref modifiers, layout: null);

				if (virtualKey == 0 || MouseUtils.IsWheelVK(virtualKey))
					return Errors.ValueErrorOccurred("Expected a keyboard key or mouse button.", KeyName);

				if (owner.HookThread.TryGetDeviceKeyState(device, virtualKey, out var down))
					return down;
				return Errors.OSErrorOccurredWithMessage("Physical key state is unavailable for this input source or the device is disconnected.");
			}

			public static object staticget_Devices(object @this)
			{
				try
				{
					var owner = Script.TheScript;
					var devices = owner.HookThread.GetInputDevices();
					var result = new Array();
					_ = result.array.EnsureCapacity(devices.Length);

					foreach (var device in devices)
						result.array.Add(new Input(device, owner));

					return result;
				}
				catch (Exception ex) when (ex is not KeysharpException and not Flow.UserRequestedExitException)
				{
					return Errors.OSErrorOccurredWithMessage(ex.Message);
				}
			}
		}
	}
}
