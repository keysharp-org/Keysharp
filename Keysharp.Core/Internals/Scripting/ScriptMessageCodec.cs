using Keysharp.Builtins;

namespace Keysharp.Internals.Scripting
{
	internal static class ScriptMessageCodec
	{
		internal static bool TryEncodeRequest(object data, long expectedProcessStartTicks, out byte[] request)
			=> TryEncode(data, false, expectedProcessStartTicks, out request);

		// An empty string leaves the message for the next hook; an empty Buffer is still a reply.
		internal static byte[] EncodeReply(object value)
		{
			return value is null or string { Length: 0 } || !TryEncode(value, true, 0, out var frame) ? null : frame;
		}

		internal static object DecodeReply(byte[] reply) => reply switch
		{
			[ScriptEndpoint.Text or ScriptEndpoint.Binary, ..] => Decode(reply[0], reply.AsSpan(1)),
			_ => ""
		};

		internal static object Decode(byte kind, ReadOnlySpan<byte> payload)
		{
			if (kind == ScriptEndpoint.Text)
				return Encoding.UTF8.GetString(payload);

			var buffer = new Keysharp.Builtins.Buffer((long)payload.Length);
			payload.CopyTo(buffer.AsSpan());
			return buffer;
		}

		private static unsafe bool TryEncode(object value, bool reply, long expectedProcessStartTicks, out byte[] frame)
		{
			frame = null;
			var binary = value is Any;
			string text = null;
			long size, ptr = 0;

			if (binary)
			{
				if (value is Keysharp.Builtins.Buffer buffer)
				{
					size = buffer.size;
					ptr = buffer.Ptr;
				}
				else
				{
					if (Script.GetPropertyValueOrNull((Any)value, "Size") is not { } sizeValue || !TryGetBufferPointer(value, out ptr))
					{
						_ = Errors.TypeErrorOccurred(value, typeof(Keysharp.Builtins.Buffer));
						return false;
					}

					if (!sizeValue.CoerceLong(out size))
						return false;
				}

				if (size < 0 || size > 0 && (ulong)ptr < 65536)
				{
					_ = Errors.ValueErrorOccurred("Invalid buffer.");
					return false;
				}
			}
			else
			{
				if (!value.CoerceString(out text))
					return false;

				size = text.Length > ScriptEndpoint.MaxPayload ? text.Length : Encoding.UTF8.GetByteCount(text);
			}

			if (size > ScriptEndpoint.MaxPayload)
			{
				_ = Errors.ValueErrorOccurred($"A message can hold at most {ScriptEndpoint.MaxPayload} bytes.");
				return false;
			}

			frame = reply ? new byte[(int)size + 1] : ScriptEndpoint.MessageRequest(binary, (int)size, expectedProcessStartTicks);

			if (reply)
				frame[0] = binary ? ScriptEndpoint.Binary : ScriptEndpoint.Text;

			var payload = frame.AsSpan(frame.Length - (int)size);

			if (binary)
				new ReadOnlySpan<byte>((void*)ptr, (int)size).CopyTo(payload);
			else
				_ = Encoding.UTF8.GetBytes(text, payload);

			GC.KeepAlive(value);
			return true;
		}

		// A zero pointer is valid for an empty Buffer, while a missing Ptr is still a type error.
		private static bool TryGetBufferPointer(object value, out long ptr)
		{
			if (value is IPointable pointable)
			{
				ptr = pointable.Ptr;
				return true;
			}

			ptr = 0;
			return Script.GetPropertyValueOrNull((Any)value, "Ptr") is { } pointer && pointer.TryCoerceLong(out ptr);
		}
	}
}
