#if LINUX
using System.Reflection;
using System.Runtime.InteropServices;
using Keysharp.Internals;
using Keysharp.Internals.Linux;
using Keysharp.Internals.Input.Linux;

namespace Keysharp.Tests
{
	[TestFixture, Category("Internal"), Category("Curated")]
	public unsafe class LinuxInputCallbackTests
	{
		[Test]
		public void ModifiedReplyRemainsOwnedAfterManagedCallbackReturns()
		{
			const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
			var type = typeof(KeysharpInputClient);
			var client = (KeysharpInputClient)type.GetConstructors(flags).Single().Invoke(
				[nint.Zero, KeysharpInputClient.ConnectionRole.CallbackStream,
				 LinuxPermissionScope.InputControl, KeysharpInputClient.Operations.BlockInput]);
			var callback = type.GetMethod("HandleNestedHook", flags);
			var parameters = callback.GetParameters();
			var eventType = parameters[0].ParameterType.GetElementType();
			var replyType = parameters[1].ParameterType.GetElementType();
			var hookEvent = NativeMemory.AllocZeroed((nuint)Marshal.SizeOf(eventType));
			var reply = NativeMemory.AllocZeroed((nuint)Marshal.SizeOf(replyType));
			var handlerField = type.GetField("nestedHookEventHandler", flags);
			var buffers = (nint[])type.GetField("nestedReplacementBuffers", flags).GetValue(client);
			try
			{
				Marshal.WriteInt32((nint)hookEvent, 4, (int)KeysharpInputClient.HookType.KeyboardLowLevel);
				Marshal.WriteInt64((nint)hookEvent, 8, 42);
				handlerField.SetValue(client, (Action<KeysharpInputClient, KeysharpInputClient.HookEvent>)
					((sender, message) => sender.SendHookDecision(message.EventId,
						KeysharpInputClient.HookDecision.Modify, [KeysharpInputClient.Input.Key(65)])));
				object[] arguments = [Pointer.Box(hookEvent, parameters[0].ParameterType),
					Pointer.Box(reply, parameters[1].ParameterType)];
				callback.Invoke(client, arguments);

				// The native serializer reads this pointer only after the delegate has returned.
				var replacement = Marshal.ReadIntPtr((nint)reply, 8);
				Assert.That(replacement, Is.Not.EqualTo(nint.Zero));
				Assert.That(buffers[0], Is.EqualTo(replacement), "The callback released native reply storage too early.");
				Assert.That(Marshal.ReadInt16(replacement, 8), Is.EqualTo(65));

				handlerField.SetValue(client, null);
				callback.Invoke(client, arguments);
				Assert.That(buffers[0], Is.EqualTo(nint.Zero));
				Assert.That(Marshal.ReadIntPtr((nint)reply, 8), Is.EqualTo(nint.Zero));
			}
			finally
			{
				foreach (var buffer in buffers) NativeMemory.Free((void*)buffer);
				NativeMemory.Free(reply);
				NativeMemory.Free(hookEvent);
			}
		}
	}
}
#endif
