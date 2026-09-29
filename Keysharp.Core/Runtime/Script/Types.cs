using Keysharp.Builtins;
using StringBuffer = Keysharp.Builtins.Ks.StringBuffer;

namespace Keysharp.Runtime
{
	public partial class Script
	{
		public static bool IsTypeOrBase(Type t1, string t2)
		{
			while (t1 != null)
			{
				var nameToUse = t1.FullName;

				if (!string.IsNullOrEmpty(t1.Namespace))
					nameToUse = nameToUse.TrimStartOf($"{t1.Namespace}.").Replace('+', '.').TrimStartOf(Keywords.MainClassName + ".");

				if (string.Compare(nameToUse, t2, true) == 0)
					return true;

				t1 = t1.BaseType;
			}

			return false;
		}

		private static void MatchTypes(ref object left, ref object right)
		{
			if (left is bool bl)
				left = bl ? 1L : 0L;
			if (right is bool br)
				right = br ? 1L : 0L;
			if (left is StringBuffer sbl)
				left = sbl.ToString();
			if (right is StringBuffer sbr)
				right = sbr.ToString();

			var lt = left.GetType();
			var rt = right.GetType();
			if (lt == rt)
				return;

			if (left is Any || right is Any)
				return;

			if (ParseNumericArgs(left, right, "value compare", out bool leftIsDouble, out bool rightIsDouble, out double leftd, out long leftl, out double rightd, out long rightl, false))
			{
				if (leftIsDouble && rightIsDouble)
				{
					left = leftd; right = rightd;
					return;
				}
				else if (!leftIsDouble && !rightIsDouble)
				{
					left = leftl; right = rightl;
					return;
				}
				else if (!leftIsDouble)
				{
					left = (double)leftl; right = rightd;
					return;
				}
				else if (!rightIsDouble)
				{
					left = leftd; right = (double)rightl;
					return;
				}
			}

			if (left is string || right is string)
			{
				if (!left.TryCoerceString(out var leftText) || !right.TryCoerceString(out var rightText))
					return;
				left = leftText;
				right = rightText;
				return;
			}
			else if (left is double || right is double)
			{
				if (!left.TryCoerceDouble(out var leftAsDouble) || !right.TryCoerceDouble(out var rightAsDouble))
					return;
				left = leftAsDouble;
				right = rightAsDouble;
				return;
			}
			else if (left is long || right is long)
			{
				if (!left.TryCoerceLong(out var leftAsLong) || !right.TryCoerceLong(out var rightAsLong))
					return;
				left = leftAsLong;
				right = rightAsLong;
				return;
			}
		}
	}
}
