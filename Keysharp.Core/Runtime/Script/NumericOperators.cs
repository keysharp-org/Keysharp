using Keysharp.Builtins;

namespace Keysharp.Runtime;

internal interface INumericOperator
{
	static abstract OperatorKind Kind { get; }
	static virtual bool IntegerOnly => false;
	static virtual bool CompareStrings => false;
	static abstract object Apply(long left, long right);
	static virtual object Apply(double left, double right) => throw new InvalidOperationException();
	static virtual object Compare(string left, string right) => throw new InvalidOperationException();
}

internal interface IUnaryNumericOperator
{
	static abstract OperatorKind Kind { get; }
	static virtual bool IntegerOnly => false;
	static abstract object Apply(long value);
	static virtual object Apply(double value) => throw new InvalidOperationException();
}

internal static class NumericOperators
{
	// Value-type policies specialize this code per operation; no delegates or operation switches execute here.
	internal static object Binary<T>(object left, object right) where T : struct, INumericOperator
	{
		if (T.IntegerOnly)
		{
			if (left is long l && right is long r) return T.Apply(l, r);
			return IntegerOperands<T>(left, right);
		}
		if (left is long integer) return Integer<T>(integer, right);
		if (left is double floating) return Float<T>(floating, right);
		if (left is bool boolean) return Integer<T>(boolean ? 1L : 0L, right);
		return Other<T>(left, right);
	}

	internal static object Integer<T>(long left, object right) where T : struct, INumericOperator
	{
		if (right is long integer) return T.Apply(left, integer);
		if (right is double floating) return T.Apply((double)left, floating);
		return ConvertRight<T, long>(left, right);
	}

	internal static object Float<T>(double left, object right) where T : struct, INumericOperator
	{
		if (right is double floating) return T.Apply(left, floating);
		if (right is long integer) return T.Apply(left, (double)integer);
		return ConvertRight<T, double>(left, right);
	}

	private static object ConvertRight<T, TLeft>(TLeft left, object right) where T : struct, INumericOperator where TLeft : struct
	{
		if (right == null) return Unset(T.Kind, "Right");
		if (right is bool boolean) return ApplyRight<T, TLeft>(left, boolean ? 1L : 0L);
		if (right is string text)
		{
			var span = text.AsSpan().Trim();
			if (long.TryParse(span, out var integer)) return ApplyRight<T, TLeft>(left, integer);
			if (span.Contains('.'))
			{
				if (double.TryParse(span, out var floating)) return ApplyRight<T, TLeft>(left, floating);
				return Errors.TypeErrorOccurred(right, typeof(double), Script.DefaultObject);
			}
		}
		if (right.TryParseLong(out var integerValue)) return ApplyRight<T, TLeft>(left, integerValue);
		if (right.TryParseDouble(out var floatValue, true)) return ApplyRight<T, TLeft>(left, floatValue);
		return Errors.TypeErrorOccurred(right, typeof(double), Script.DefaultObject);
	}

	// The JIT removes these type tests and casts for each value-type specialization.
	private static object ApplyRight<T, TLeft>(TLeft left, long right) where T : struct, INumericOperator where TLeft : struct =>
		typeof(TLeft) == typeof(long) ? T.Apply((long)(object)left, right) : T.Apply((double)(object)left, (double)right);

	private static object ApplyRight<T, TLeft>(TLeft left, double right) where T : struct, INumericOperator where TLeft : struct =>
		T.Apply(typeof(TLeft) == typeof(long) ? (double)(long)(object)left : (double)(object)left, right);

	private static object Other<T>(object left, object right) where T : struct, INumericOperator
	{
		if (left == null) return Unset(T.Kind, "Left");
		if (right == null) return Unset(T.Kind, "Right");
		if (T.CompareStrings && left is string ls && right is string rs) return T.Compare(ls, rs);
		if (left is Any) return Script.TheScript.Operators.Invoke(T.Kind, left, right);
		if (left.TryParseLong(out var integer)) return Integer<T>(integer, right);
		if (left.TryParseDouble(out var floating, true)) return Float<T>(floating, right);
		return Errors.TypeErrorOccurred(left, typeof(double), Script.DefaultObject);
	}

	private static object IntegerOperands<T>(object left, object right) where T : struct, INumericOperator
	{
		if (left == null) return Unset(T.Kind, "Left");
		if (right == null) return Unset(T.Kind, "Right");
		if (left is Any) return Script.TheScript.Operators.Invoke(T.Kind, left, right);
		if (!Script.ParseNumericArgs(left, right, OperatorCatalog.Get(T.Kind).Description,
			out var ld, out var rd, out _, out var l, out _, out var r)) return Script.DefaultObject;
		if (ld) return Errors.TypeErrorOccurred(left, typeof(long));
		if (rd) return Errors.TypeErrorOccurred(right, typeof(long));
		return T.Apply(l, r);
	}

	internal static object Unary<T>(object value) where T : struct, IUnaryNumericOperator
	{
		if (value is long integer) return T.Apply(integer);
		if (!T.IntegerOnly && value is double floating) return T.Apply(floating);
		return UnaryOther<T>(value);
	}

	private static object UnaryOther<T>(object value) where T : struct, IUnaryNumericOperator
	{
		if (value == null) return Unset(T.Kind, "Right");
		if (value is Any && Script.TheScript.Operators.TryInvoke(T.Kind, value, null, out var result)) return result;
		if (value is double && T.IntegerOnly) return Errors.TypeErrorOccurred(value, typeof(long), Script.DefaultObject);
		if (value.TryParseLong(out var integer)) return T.Apply(integer);
		if (!T.IntegerOnly && value.TryParseDouble(out var floating, true)) return T.Apply(floating);
		return Errors.TypeErrorOccurred(value, T.IntegerOnly ? typeof(long) : typeof(double), Script.DefaultObject);
	}

	private static object Unset(OperatorKind kind, string side) =>
		Errors.UnsetErrorOccurred($"{side} side operand of {OperatorCatalog.Get(kind).Description}", Script.DefaultObject);

}
