using Keysharp.Builtins;

namespace Keysharp.Runtime;

internal static class NumericOperation
{
	internal readonly struct Add : INumericOperator
	{
		public static OperatorKind Kind => OperatorKind.Add;
		public static object Apply(long left, long right) => left + right;
		public static object Apply(double left, double right) => left + right;
	}

	internal readonly struct Subtract : INumericOperator
	{
		public static OperatorKind Kind => OperatorKind.Subtract;
		public static object Apply(long left, long right) => left - right;
		public static object Apply(double left, double right) => left - right;
	}

	internal readonly struct Multiply : INumericOperator
	{
		public static OperatorKind Kind => OperatorKind.Multiply;
		public static object Apply(long left, long right) => left * right;
		public static object Apply(double left, double right) => left * right;
	}

	internal readonly struct Divide : INumericOperator
	{
		public static OperatorKind Kind => OperatorKind.Divide;
		public static object Apply(long left, long right) => right == 0 ? Errors.ZeroDivisionErrorOccurred("Right side operand of floating point division") : (object)((double)left / right);
		public static object Apply(double left, double right) => right == 0 ? Errors.ZeroDivisionErrorOccurred("Right side operand of floating point division") : (object)(left / right);
	}

	internal readonly struct FloorDivide : INumericOperator
	{
		public static OperatorKind Kind => OperatorKind.FloorDivide;
		public static bool IntegerOnly => true;
		public static object Apply(long left, long right) => right == 0 ? Errors.ZeroDivisionErrorOccurred("Right side operand of floor divide") : (object)(left / right);
	}

	internal readonly struct Power : INumericOperator
	{
		public static OperatorKind Kind => OperatorKind.Power;
		public static object Apply(long left, long right) => (long)Math.Pow(left, right);
		public static object Apply(double left, double right) => Math.Pow(left, right);
	}

	internal readonly struct BitwiseAnd : INumericOperator
	{
		public static OperatorKind Kind => OperatorKind.BitwiseAnd;
		public static bool IntegerOnly => true;
		public static object Apply(long left, long right) => left & right;
	}

	internal readonly struct BitwiseOr : INumericOperator
	{
		public static OperatorKind Kind => OperatorKind.BitwiseOr;
		public static bool IntegerOnly => true;
		public static object Apply(long left, long right) => left | right;
	}

	internal readonly struct BitwiseXor : INumericOperator
	{
		public static OperatorKind Kind => OperatorKind.BitwiseXor;
		public static bool IntegerOnly => true;
		public static object Apply(long left, long right) => left ^ right;
	}

	internal readonly struct BitShiftLeft : INumericOperator
	{
		public static OperatorKind Kind => OperatorKind.BitShiftLeft;
		public static bool IntegerOnly => true;
		public static object Apply(long left, long right) => Shift(left, right, OperatorKind.BitShiftLeft);
	}

	internal readonly struct BitShiftRight : INumericOperator
	{
		public static OperatorKind Kind => OperatorKind.BitShiftRight;
		public static bool IntegerOnly => true;
		public static object Apply(long left, long right) => Shift(left, right, OperatorKind.BitShiftRight);
	}

	internal readonly struct LogicalBitShiftRight : INumericOperator
	{
		public static OperatorKind Kind => OperatorKind.LogicalBitShiftRight;
		public static bool IntegerOnly => true;
		public static object Apply(long left, long right) => Shift(left, right, OperatorKind.LogicalBitShiftRight);
	}

	internal readonly struct LessThan : INumericOperator
	{
		public static OperatorKind Kind => OperatorKind.LessThan;
		public static object Apply(long left, long right) => left < right;
		public static object Apply(double left, double right) => left < right;
		public static bool CompareStrings => true;
		public static object Compare(string left, string right) => Strings.StrCmp(left, right, true) < 0;
	}

	internal readonly struct LessThanOrEqual : INumericOperator
	{
		public static OperatorKind Kind => OperatorKind.LessThanOrEqual;
		public static object Apply(long left, long right) => left <= right;
		public static object Apply(double left, double right) => left <= right;
		public static bool CompareStrings => true;
		public static object Compare(string left, string right) => Strings.StrCmp(left, right, true) <= 0;
	}

	internal readonly struct GreaterThan : INumericOperator
	{
		public static OperatorKind Kind => OperatorKind.GreaterThan;
		public static object Apply(long left, long right) => left > right;
		public static object Apply(double left, double right) => left > right;
		public static bool CompareStrings => true;
		public static object Compare(string left, string right) => Strings.StrCmp(left, right, true) > 0;
	}

	internal readonly struct GreaterThanOrEqual : INumericOperator
	{
		public static OperatorKind Kind => OperatorKind.GreaterThanOrEqual;
		public static object Apply(long left, long right) => left >= right;
		public static object Apply(double left, double right) => left >= right;
		public static bool CompareStrings => true;
		public static object Compare(string left, string right) => Strings.StrCmp(left, right, true) >= 0;
	}

	private static object Shift(long left, long right, OperatorKind kind)
	{
		var count = (int)right;
		if (count < 0 || count > 63)
			return InvalidShift(count, kind);
		return kind switch
		{
			OperatorKind.BitShiftLeft => left << count,
			OperatorKind.BitShiftRight => left >> count,
			_ => (long)((ulong)left >> count)
		};
	}

	private static object InvalidShift(int count, OperatorKind kind) =>
		Errors.ErrorOccurred($"Shift operand of {count} for {OperatorCatalog.Get(kind).Description} was not in the range of [0-63].");

	internal readonly struct Negate : IUnaryNumericOperator
	{
		public static OperatorKind Kind => OperatorKind.Minus;
		public static object Apply(long value) => -value;
		public static object Apply(double value) => 0.0 - value;
	}

	internal readonly struct Complement : IUnaryNumericOperator
	{
		public static OperatorKind Kind => OperatorKind.BitwiseNot;
		public static bool IntegerOnly => true;
		public static object Apply(long value) => ~value;
	}
}
