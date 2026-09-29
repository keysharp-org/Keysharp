using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Keysharp.Tests
{
	// The defaulting conversion is an internal API; script string arguments use the strict conversion.
	public class StringConversionInternalsTests : TestRunner
	{
		[Test, Category("Internal"), Category("Curated")]
		public void DefaultsAndScalarResults()
		{
			Assert.AreEqual("fallback", Lenient(null, "fallback"));
			Assert.AreEqual("", Lenient(null, null));
			Assert.AreEqual("fallback", Lenient(new KeysharpObject(), "fallback"));
			Assert.AreEqual("", Lenient(WithToString(() => ""), "fallback"));
			Assert.AreEqual("fallback", Lenient(WithToString(() => null), "fallback"));
			Assert.AreEqual("1.0", Lenient(WithToString(() => 1.0)));
			Assert.AreEqual("1", Lenient(true));
			Assert.AreEqual("1.234", Lenient(1.234));

			var calls = 0;
			var nested = WithToString(() => { calls++; return "nested"; });
			Assert.AreEqual("fallback", Lenient(WithToString(() => nested), "fallback"));
			Assert.AreEqual(0, calls);
		}

		[Test, Category("Internal"), Category("Curated")]
		public void DefaultingContainsCallbackErrorsAndRestoresTryState()
		{
			var errorsSeen = 0;
			_ = Errors.OnError(new KeysharpFunc((Func<object, object, object>)((_, _) => { errorsSeen++; return 0L; })));
			var value = WithToString(() => Errors.ValueErrorOccurred("conversion failed"));
			var tv = Threads.Current;
			var insideTry = tv.insideTry;
			var caught = tv.caughtException;

			Assert.AreEqual("fallback", Lenient(value, "fallback"));
			Assert.AreEqual("fallback", Lenient(new ScriptErrorClrValue(), "fallback"));
			Assert.IsFalse(value.TryCoerceString(out _));
			Assert.IsFalse(new ThrowingClrValue().TryCoerceString(out _));
			Assert.AreEqual(0, errorsSeen);
			Assert.AreEqual(insideTry, tv.insideTry);
			Assert.AreSame(caught, tv.caughtException);
			Assert.AreEqual("fallback", Lenient(new ThrowingClrValue(), "fallback"));
		}

		[Test, Category("Internal"), Category("Curated")]
		public void StrictConversionPreservesErrors()
		{
			using var scope = Keysharp.Runtime.Flow.EnterTry();
			var unsupported = new KeysharpObject();
			Assert.IsFalse(unsupported.TryCoerceString(out _));
			Assert.IsInstanceOf<TypeError>(Assert.Throws<KeysharpException>(() => unsupported.CoerceString(out _)).UserError);
			var failure = Assert.Throws<KeysharpException>(() =>
				WithToString(() => Errors.ValueErrorOccurred("conversion failed")).CoerceString(out _));
			Assert.IsInstanceOf<ValueError>(failure.UserError);
			Assert.AreEqual("conversion failed", failure.Message);
			Assert.Throws<InvalidOperationException>(() => new ThrowingClrValue().CoerceString(out _));
		}

		[Test, Category("Internal"), Category("Curated")]
		public void StrictConversionReportsAContinuedError()
		{
			_ = Errors.OnError(new KeysharpFunc((Func<object, object, object>)((_, _) => -1L)));
			Assert.IsFalse(new KeysharpObject().CoerceString(out var text, "fallback"));
			Assert.AreEqual("fallback", text);
			Assert.IsFalse(new KeysharpObject().CoerceLong(out var number));
			Assert.AreEqual(0L, number);
			Assert.IsFalse(new KeysharpObject().CoerceDouble(out _));
		}

		[Test, Category("Internal"), Category("Curated")]
		public void DefaultingPreservesScriptExit()
		{
			Assert.Throws<Keysharp.Builtins.Flow.UserRequestedExitException>(() =>
				WithToString(() => throw new Keysharp.Builtins.Flow.UserRequestedExitException()).TryCoerceString(out _));
		}

		[Test, Category("Internal"), Category("Curated")]
		public void DiagnosticsDoNotCallConversionHooks()
		{
			var calls = 0;
			var value = WithToString(() => { calls++; return "custom"; });
			Assert.AreEqual("Object", Errors.Describe(value));
			Assert.AreEqual("[Object]", Errors.Describe(new Keysharp.Builtins.Array(new object[] { value })));
			Assert.AreEqual(0, calls);
			Assert.AreEqual(nameof(ThrowingClrValue), Errors.Describe(new ThrowingClrValue()));
			Assert.AreEqual(Types.Type(value), value.ToString());
			Assert.AreEqual("Lock", new Ks.KeysharpLock().ToString());
			Assert.AreEqual("123", Errors.Describe(123));
			Assert.AreEqual(nameof(ThrowingClrValue), new Error(new ThrowingClrValue()).Message);

			var prototype = new KeysharpObject();
			prototype.DefinePropInternal("__Class", new OwnPropsDesc(prototype,
				set_Get: new KeysharpFunc((Func<object, object>)(_ => { calls++; return "custom"; }))));
			value.SetBaseInternal(prototype);
			using var scope = Keysharp.Runtime.Flow.EnterTry();
			Assert.AreEqual("Object", Errors.Describe(value));
			Assert.Throws<KeysharpException>(() => Errors.MissingPropertyErrorOccurred(value, "missing"));
			Assert.Throws<KeysharpException>(() => Errors.MissingMethodErrorOccurred(value, "missing"));
			Assert.AreEqual(0, calls);
		}

		[Test, Category("Internal"), Category("Curated")]
		public void NumericFamiliesTakeTheDefaultForNoValue()
		{
			Assert.IsFalse(((object)null).TryCoerceLong(out var none, 7L));
			Assert.AreEqual(7L, none);
			Assert.IsTrue(((object)null).CoerceLong(out var omitted, 7L));
			Assert.AreEqual(7L, omitted);
			Assert.IsTrue(((object)"12").CoerceInt(out var parsed));
			Assert.AreEqual(12, parsed);
			Assert.IsFalse(((object)"").TryCoerceDouble(out var empty, 1.5));
			Assert.AreEqual(1.5, empty);

			using var scope = Keysharp.Runtime.Flow.EnterTry();
			Assert.IsInstanceOf<TypeError>(Assert.Throws<KeysharpException>(() => ((object)"").CoerceLong(out _)).UserError);
		}

		[Test, Category("Internal"), Category("Curated")]
		public void NumericTryCoercionContainsClrToStringErrors()
		{
			var value = new ThrowingClrValue();
			Assert.IsFalse(value.TryCoerceLong(out var integer, 7L));
			Assert.AreEqual(7L, integer);
			Assert.IsFalse(value.TryCoerceInt(out var small, 9));
			Assert.AreEqual(9, small);
			Assert.IsFalse(value.TryCoerceDouble(out var floating, 1.5));
			Assert.AreEqual(1.5, floating);

			var calls = 0;
			_ = Errors.OnError(new KeysharpFunc((Func<object, object, object>)((_, _) => { calls++; return 0L; })));
			Assert.IsFalse(new ScriptErrorClrValue().TryCoerceLong(out _));
			Assert.IsFalse(new ScriptErrorClrValue().TryCoerceDouble(out _));
			Assert.AreEqual(0, calls);

			Assert.Throws<Keysharp.Builtins.Flow.UserRequestedExitException>(() => new ExitingClrValue().TryCoerceLong(out _));
			Assert.Throws<Keysharp.Builtins.Flow.UserRequestedExitException>(() => new ExitingClrValue().TryCoerceDouble(out _));
		}

		private static string Lenient(object value, string def = "")
		{
			_ = value.TryCoerceString(out var text, def);
			return text;
		}

		private static KeysharpObject WithToString(Func<object> callback)
		{
			var value = new KeysharpObject();
			value.DefinePropInternal("ToString", new OwnPropsDesc(value,
				set_Call: new KeysharpFunc((Func<object, object>)(_ => callback()))));
			return value;
		}

		private sealed class ThrowingClrValue
		{
			public override string ToString() => throw new InvalidOperationException("conversion failed");
		}

		private sealed class ScriptErrorClrValue
		{
			public override string ToString()
			{
				_ = Errors.ValueErrorOccurred("conversion failed");
				return "continued";
			}
		}

		private sealed class ExitingClrValue
		{
			public override string ToString() => throw new Keysharp.Builtins.Flow.UserRequestedExitException();
		}
	}
}
