namespace Keysharp.Tests;

// The defaulting conversion is an internal API; script string arguments use the strict conversion.
public class StringConversionInternalsTests : TestRunner
{
	[Test, Category("Internal"), Category("Curated")]
	public void DefaultsAndScalarResults()
	{
		Assert.That(Lenient(null, "fallback"), Is.EqualTo("fallback"));
		Assert.That(Lenient(null, null), Is.Empty);
		Assert.That(Lenient(new KeysharpObject(), "fallback"), Is.EqualTo("fallback"));
		Assert.That(Lenient(WithToString(() => ""), "fallback"), Is.Empty);
		Assert.That(Lenient(WithToString(() => null), "fallback"), Is.EqualTo("fallback"));
		Assert.That(Lenient(WithToString(() => 1.0)), Is.EqualTo("1.0"));
		Assert.That(Lenient(true), Is.EqualTo("1"));
		Assert.That(Lenient(1.234), Is.EqualTo("1.234"));

		var calls = 0;
		var nested = WithToString(() => { calls++; return "nested"; });
		Assert.That(Lenient(WithToString(() => nested), "fallback"), Is.EqualTo("fallback"));
		Assert.That(calls, Is.EqualTo(0));
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

		Assert.That(Lenient(value, "fallback"), Is.EqualTo("fallback"));
		Assert.That(Lenient(new ScriptErrorClrValue(), "fallback"), Is.EqualTo("fallback"));
		Assert.That(value.TryCoerceString(out _), Is.False);
		Assert.That(new ThrowingClrValue().TryCoerceString(out _), Is.False);
		Assert.That(errorsSeen, Is.EqualTo(0));
		Assert.That(tv.insideTry, Is.EqualTo(insideTry));
		Assert.That(tv.caughtException, Is.SameAs(caught));
		Assert.That(Lenient(new ThrowingClrValue(), "fallback"), Is.EqualTo("fallback"));
	}

	[Test, Category("Internal"), Category("Curated")]
	public void StrictConversionPreservesErrors()
	{
		using var scope = Keysharp.Runtime.Flow.EnterTry();
		var unsupported = new KeysharpObject();
		Assert.That(unsupported.TryCoerceString(out _), Is.False);
		Assert.IsInstanceOf<TypeError>(Assert.Throws<KeysharpException>(() => unsupported.CoerceString(out _)).UserError);
		var failure = Assert.Throws<KeysharpException>(() =>
			WithToString(() => Errors.ValueErrorOccurred("conversion failed")).CoerceString(out _));
		Assert.IsInstanceOf<ValueError>(failure.UserError);
		Assert.That(failure.Message, Is.EqualTo("conversion failed"));
		_ = Assert.Throws<InvalidOperationException>(() => new ThrowingClrValue().CoerceString(out _));
	}

	[Test, Category("Internal"), Category("Curated")]
	public void StrictConversionReportsAContinuedError()
	{
		_ = Errors.OnError(new KeysharpFunc((Func<object, object, object>)((_, _) => -1L)));
		Assert.That(new KeysharpObject().CoerceString(out var text, "fallback"), Is.False);
		Assert.That(text, Is.EqualTo("fallback"));
		Assert.That(new KeysharpObject().CoerceLong(out var number), Is.False);
		Assert.That(number, Is.EqualTo(0L));
		Assert.That(new KeysharpObject().CoerceDouble(out _), Is.False);
	}

	[Test, Category("Internal"), Category("Curated")]
	public void DefaultingPreservesScriptExit()
	{
		_ = Assert.Throws<Keysharp.Builtins.Flow.UserRequestedExitException>(() =>
			WithToString(() => throw new Keysharp.Builtins.Flow.UserRequestedExitException()).TryCoerceString(out _));
	}

	[Test, Category("Internal"), Category("Curated")]
	public void DiagnosticsDoNotCallConversionHooks()
	{
		var calls = 0;
		var value = WithToString(() => { calls++; return "custom"; });
		Assert.That(Errors.Describe(value), Is.EqualTo("Object"));
		Assert.That(Errors.Describe(new Keysharp.Builtins.Array([value])), Is.EqualTo("[Object]"));
		Assert.That(calls, Is.EqualTo(0));
		Assert.That(Errors.Describe(new ThrowingClrValue()), Is.EqualTo(nameof(ThrowingClrValue)));
		Assert.That(value.ToString(), Is.EqualTo(Types.Type(value)));
		Assert.That(new Ks.KeysharpLock().ToString(), Is.EqualTo("Lock"));
		Assert.That(Errors.Describe(123), Is.EqualTo("123"));
		Assert.That(new Error(new ThrowingClrValue()).Message, Is.EqualTo(nameof(ThrowingClrValue)));

		var prototype = new KeysharpObject();
		prototype.DefinePropInternal("__Class", new OwnPropsDesc(
			set_Get: new KeysharpFunc((Func<object, object>)(_ => { calls++; return "custom"; }))));
		value.SetBaseInternal(prototype);
		using var scope = Keysharp.Runtime.Flow.EnterTry();
		Assert.That(Errors.Describe(value), Is.EqualTo("Object"));
		_ = Assert.Throws<KeysharpException>(() => Errors.MissingPropertyErrorOccurred(value, "missing"));
		_ = Assert.Throws<KeysharpException>(() => Errors.MissingMethodErrorOccurred(value, "missing"));
		Assert.That(calls, Is.EqualTo(0));
	}

	[Test, Category("Internal"), Category("Curated")]
	public void NumericFamiliesTakeTheDefaultForNoValue()
	{
		Assert.That(((object)null).TryCoerceLong(out var none, 7L), Is.False);
		Assert.That(none, Is.EqualTo(7L));
		Assert.IsTrue(((object)null).CoerceLong(out var omitted, 7L));
		Assert.That(omitted, Is.EqualTo(7L));
		Assert.IsTrue(((object)"12").CoerceInt(out var parsed));
		Assert.That(parsed, Is.EqualTo(12));
		Assert.That(((object)"").TryCoerceDouble(out var empty, 1.5), Is.False);
		Assert.That(empty, Is.EqualTo(1.5));

		using var scope = Keysharp.Runtime.Flow.EnterTry();
		Assert.IsInstanceOf<TypeError>(Assert.Throws<KeysharpException>(() => ((object)"").CoerceLong(out _)).UserError);
	}

	[Test, Category("Internal"), Category("Curated")]
	public void NumericTryCoercionContainsClrToStringErrors()
	{
		var value = new ThrowingClrValue();
		Assert.That(value.TryCoerceLong(out var integer, 7L), Is.False);
		Assert.That(integer, Is.EqualTo(7L));
		Assert.That(value.TryCoerceInt(out var small, 9), Is.False);
		Assert.That(small, Is.EqualTo(9));
		Assert.That(value.TryCoerceDouble(out var floating, 1.5), Is.False);
		Assert.That(floating, Is.EqualTo(1.5));

		var calls = 0;
		_ = Errors.OnError(new KeysharpFunc((Func<object, object, object>)((_, _) => { calls++; return 0L; })));
		Assert.That(new ScriptErrorClrValue().TryCoerceLong(out _), Is.False);
		Assert.That(new ScriptErrorClrValue().TryCoerceDouble(out _), Is.False);
		Assert.That(calls, Is.EqualTo(0));

		_ = Assert.Throws<Keysharp.Builtins.Flow.UserRequestedExitException>(() => new ExitingClrValue().TryCoerceLong(out _));
		_ = Assert.Throws<Keysharp.Builtins.Flow.UserRequestedExitException>(() => new ExitingClrValue().TryCoerceDouble(out _));
	}

	private static string Lenient(object value, string def = "")
	{
		_ = value.TryCoerceString(out var text, def);
		return text;
	}

	private static KeysharpObject WithToString(Func<object> callback)
	{
		var value = new KeysharpObject();
		value.DefinePropInternal("ToString", new OwnPropsDesc(
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
