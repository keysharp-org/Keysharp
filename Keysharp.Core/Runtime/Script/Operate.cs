using Keysharp.Builtins;
namespace Keysharp.Runtime
{
	public partial class Script
	{
		const string Keyword_Addition = "addition";
		const string Keyword_Divide = "division";
		const string Keyword_Multiply = "multiplication";
		const string Keyword_Subtraction = "subtraction";
		const string Keyword_ArLeftShift = "arithmetic left shift";
		const string Keyword_ArRightShift = "arithmetic right shift";
		const string Keyword_LogicalRightShift = "logical right shift";
		const string Keyword_BitwiseAnd = "bitwise and";
		const string Keyword_BitwiseOr = "bitwise or";
		const string Keyword_BitwiseXor = "bitwise xor";
		const string Keyword_LessThan = "less than";
		const string Keyword_LessThanOrEqual = "less than or equal";
		const string Keyword_GreaterThan = "greater than";
		const string Keyword_GreaterThanOrEqual = "greater than or equal";
		const string Keyword_Modulo = "modulo";
		const string Keyword_Power = "power";
		const string Keyword_Between = "between";
		const string Keyword_In = "in";
		const string Keyword_Contains = "contains";
		const string Keyword_Is = "is";
		const char Delimiter = ',';
		const string Keyword_And = " and ";
		const string Keyword_Integer = "integer";
		const string Keyword_Float = "float";
		const string Keyword_Number = "number";
		const string Keyword_Digit = "digit";
		const string Keyword_Xdigit = "xdigit";
		const string Keyword_Alpha = "alpha";
		const string Keyword_Upper = "upper";
		const string Keyword_Lower = "lower";
		const string Keyword_Alnum = "alnum";
		const string Keyword_Space = "space";
		const string Keyword_Time = "time";

		public static bool IfLegacy(object subject, string op, string test, bool not = false)
		{
			string variable = null;
			ReadOnlySpan<char> varspan = null;
			if (op != Keyword_Is)
			{
				variable = ForceString(subject);
				varspan = variable.AsSpan();
			}
			var ret = false;

			switch (op)
			{
				case Keyword_Between:
				{
					if (subject == null)
						return (bool)Errors.UnsetErrorOccurred($"Left side operand of between", false);

					if (test == null)
						return (bool)Errors.UnsetErrorOccurred($"Right side operand of between", false);

						var z = test.IndexOf(Keyword_And, StringComparison.OrdinalIgnoreCase);

						if (z == -1)
							z = variable.Length;

						if (double.TryParse(test.AsSpan(0, z), out var low) && double.TryParse(test.AsSpan(z + Keyword_And.Length), out var high))
						{
							var d = subject.Ad();
							ret = d >= low && d <= high;
						}
						else if (subject is string s)
						{
							ret = string.Compare(test.Substring(0, z), s) < 0 && string.Compare(s, test.Substring(z + Keyword_And.Length)) < 0;
						}
					}
					break;

				case Keyword_In:
					if (subject == null)
						return (bool)Errors.UnsetErrorOccurred($"Left side operand of in", false);

					if (test == null)
						return (bool)Errors.UnsetErrorOccurred($"Right side operand of in", false);

					foreach (Range r in test.AsSpan().Split(Delimiter))
					{
						var sub = test.AsSpan(r);

						if (varspan.Equals(sub, StringComparison.OrdinalIgnoreCase))
							ret = true;
					}

					break;

				case Keyword_Contains:
					if (subject == null)
						return (bool)Errors.UnsetErrorOccurred($"Left side operand of contains", false);

					if (test == null)
						return (bool)Errors.UnsetErrorOccurred($"Right side operand of contains", false);

					foreach (Range r in test.AsSpan().Split(Delimiter))
					{
						var sub = test.AsSpan(r);

						if (varspan.IndexOf(sub, StringComparison.OrdinalIgnoreCase) != -1)
							ret = true;
					}

					break;

				case Keyword_Is:
					if (test == null)
						return subject == null;

					//Put common cases first.
					switch (test)
					{
						case var x when x.Equals(Keyword_Integer, StringComparison.OrdinalIgnoreCase):
							ret = IsInteger(subject);
							goto done;

						case var x when x.Equals(Keyword_Float, StringComparison.OrdinalIgnoreCase):
							ret = IsFloat(subject);
							goto done;

						case var x when x.Equals(Keyword_Number, StringComparison.OrdinalIgnoreCase):
							ret = IsInteger(subject) || IsFloat(subject);
							goto done;

						case var x when x.Equals("string", StringComparison.OrdinalIgnoreCase):
							ret = subject is string;
							goto done;

						case var x when x.Equals("unset", StringComparison.OrdinalIgnoreCase) ||
							x.Equals("null", StringComparison.OrdinalIgnoreCase):
							ret = subject == null;
							goto done;
					}

					if (subject is Any kso)
					{
						var protos = TheScript.Vars.Prototypes;
						var matchingProtoKey = protos.Keys.FirstOrDefault(t => TypePathNoNamespace(t).Equals(test, StringComparison.OrdinalIgnoreCase));
						if (matchingProtoKey == null)
							return false;
						var targetProto = protos[matchingProtoKey];

						for (Any proto = kso; proto != null; proto = proto.Base)
						{
							if (proto == targetProto)
								return true;
						}
						return false;
					}

                    //Traverse class hierarchy to see if there is a match.
                    if (subject != null)
					{
						var type = subject.GetType();

						if (IsTypeOrBase(type, test))
						{
							ret = true;
							goto done;
						}
					}

					break;
			}

			done:
			return !not ? ret : !ret;
		}
		static string TypePathNoNamespace(Type t)
		{
			while (t.HasElementType) t = t.GetElementType();

			var script = TheScript;

			// Build "Outer.Inner" from declaring types (namespaces are not included here). A built-in whose CLR
			// name is not the name scripts use for it contributes the declared name, so `x is Object` matches
			// while `x is KeysharpObject` - naming the internal type - matches nothing.
			var names = new List<string>();
			for (var cur = t; cur != null && cur != script.ProgramType; cur = cur.DeclaringType)
			{
				if (IsModuleContainer(cur, script))
					continue;
				names.Add(Script.GetUserDeclaredName(cur) ?? cur.Name);
			}
			names.Reverse();
			return string.Join('.', names);
		}


		public static bool IfTest(object result) => ForceBool(result);

		//Binary operators

		public static object Add(object left, object right)
		{
			if (ParseNumericArgs(left, right, Keyword_Addition, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
			{
				if (firstIsDouble)
				{
					if (secondIsDouble)
						return firstd + secondd;
					else
						return firstd + secondl;
				}
				else
				{
					if (secondIsDouble)
						return firstl + secondd;
					else
						return firstl + secondl;
				}
			}

			return DefaultObject;
		}

		public static object BitShiftLeft(object left, object right)
		{
			if (ParseNumericArgs(left, right, Keyword_ArLeftShift, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
			{
				if (firstIsDouble)
					return Errors.TypeErrorOccurred(left, typeof(long));

				if (secondIsDouble)
					return Errors.TypeErrorOccurred(right, typeof(long));

				var r = (int)secondl;

				if (r < 0 || r > 63)
					return Errors.ErrorOccurred($"Shift operand of {r} for arithmetic left shift was not in the range of [0-63].");

				return firstl << r;
			}

			return DefaultObject;
		}

		public static object BitShiftRight(object left, object right)
		{
			if (ParseNumericArgs(left, right, Keyword_ArRightShift, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
			{
				if (firstIsDouble)
					return Errors.TypeErrorOccurred(left, typeof(long));

				if (secondIsDouble)
					return Errors.TypeErrorOccurred(right, typeof(long));

				var r = (int)secondl;

				if (r < 0 || r > 63)
					return Errors.ErrorOccurred($"Shift operand of {r} for arithmetic right shift was not in the range of [0-63].");

				return firstl >> r;
			}

			return DefaultObject;
		}

		public static object LogicalBitShiftRight(object left, object right)
		{
			if (ParseNumericArgs(left, right, Keyword_LogicalRightShift, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
			{
				if (firstIsDouble)
					return Errors.TypeErrorOccurred(left, typeof(long));

				if (secondIsDouble)
					return Errors.TypeErrorOccurred(right, typeof(long));

				var r = (int)secondl;

				if (r < 0 || r > 63)
					return Errors.ErrorOccurred($"Shift operand of {r} for logical right shift was not in the range of [0-63].");

				return (long)((ulong)firstl >> r);
			}

			return DefaultObject;
		}

		public static object BitwiseAnd(object left, object right)
		{
			if (ParseNumericArgs(left, right, Keyword_BitwiseAnd, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
			{
				if (firstIsDouble)
					return Errors.TypeErrorOccurred(left, typeof(long));

				if (secondIsDouble)
					return Errors.TypeErrorOccurred(right, typeof(long));

				return firstl & secondl;
			}

			return DefaultObject;
		}

		public static object BitwiseOr(object left, object right)
		{
			if (ParseNumericArgs(left, right, Keyword_BitwiseOr, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
			{
				if (firstIsDouble)
					return Errors.TypeErrorOccurred(left, typeof(long));

				if (secondIsDouble)
					return Errors.TypeErrorOccurred(right, typeof(long));

				return firstl | secondl;
			}

			return DefaultObject;
		}

		public static object BitwiseXor(object left, object right)
		{
			if (ParseNumericArgs(left, right, Keyword_BitwiseXor, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
			{
				if (firstIsDouble)
					return Errors.TypeErrorOccurred(left, typeof(long));

				if (secondIsDouble)
					return Errors.TypeErrorOccurred(right, typeof(long));

				return firstl ^ secondl;
			}

			return DefaultObject;
		}
		public static object BooleanAnd(object left, object right)
		{
			if (left == null)
				return (bool)Errors.UnsetErrorOccurred($"Left side operand of boolean and", false);

			if (right == null)
				return (bool)Errors.UnsetErrorOccurred($"Right side operand of boolean and", false);

			var b1 = ForceBool(left);

			if (!b1)
				return left;

			return right;
		}

		public static object BooleanOr(object left, object right)
		{
			if (left == null)
				return (bool)Errors.UnsetErrorOccurred($"Left side operand of boolean or", false);

			if (right == null)
				return (bool)Errors.UnsetErrorOccurred($"Right side operand of boolean or", false);

			var b1 = ForceBool(left);

			if (b1)
				return left;

			return right;
		}

		public static object Concat(object left, object right)
		{
			//Do not check the left side for null, AHK allows it.
			if (right == null)
				return (bool)Errors.UnsetErrorOccurred($"Right side operand of concat", false);

			// Guard agains accidental function object concatenation (likely used function call statement in an expression context)
			if (left is KeysharpFunc)
				return Errors.TypeErrorOccurred(left, typeof(string));

			return string.Concat(ForceString(left), ForceString(right));
		}

		public static object RegEx(object left, object right)
		{
			if (left == null)
				return (bool)Errors.UnsetErrorOccurred($"Left side operand of regular expression", false);

			if (right == null)
				return (bool)Errors.UnsetErrorOccurred($"Right side operand of regular expression", false);

			return Builtins.RegEx.RegExMatch(ForceString(left), ForceString(right));
		}

		public static object NotRegEx(object left, object right) => !ForceBool(RegEx(left, right));

		public static object FloorDivide(object left, object right)
		{
			if (ParseNumericArgs(left, right, Keyword_BitwiseOr, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
			{
				if (firstIsDouble)
					return Errors.TypeErrorOccurred(left, typeof(long));

				if (secondIsDouble)
					return Errors.TypeErrorOccurred(right, typeof(long));

				if (secondl == 0L)
					return Errors.ZeroDivisionErrorOccurred("Right side operand of floor divide");

				return firstl / secondl;
			}

			return DefaultObject;
		}

		public static object IdentityInequality(object left, object right)
		{
			if (left == null)
				return right != null;

			if (right == null)
				return left != null;

			_ = MatchTypes(ref left, ref right);

			if (left is string s1 && right is string s2)
				return Strings.StrCmp(s1, s2, true) != 0;

			return !left.Equals(right);
		}

		public static object IdentityEquality(object left, object right) //This is for a double equal sign in a conditional, and uses case sensitive comparison for strings.
		{
			if (left == null)
				return right == null;

			if (right == null)
				return left == null;

			_ = MatchTypes(ref left, ref right);

			if (left is string s1 && right is string s2)
				return Strings.StrCmp(s1, s2, true) == 0;

			return left.Equals(right);
		}

		public static object ValueEquality(object left, object right) //This is for a single equal sign in a conditional, and uses the case insensitive comparison type for strings.
		{
			if (left == null)
				return right == null;

			if (right == null)
				return left == null;

			_ = MatchTypes(ref left, ref right);

			if (left is string s1 && right is string s2)
				return Strings.StrCmp(s1, s2, false) == 0;
			else if (left is Builtins.Array al1 && right is Builtins.Array al2)
			{
				var len1 = (long)al1.Length;
				var len2 = (long)al2.Length;

				if (len1 != len2)
					return false;

				for (var i = 1; i <= len1; i++)
				{
					if (IsNumeric(al1[i]) && IsNumeric(al2[i]))
					{
						var d1 = Convert.ToDouble(al1[i]);
						var d2 = Convert.ToDouble(al2[i]);

						if (d1 != d2)
							return false;
					}
					else if (!al1[i].Equals(al2[i]))
						return false;
				}

				return true;
			}
			else if (left is Builtins.Buffer buf1 && right is Builtins.Buffer buf2)
			{
				var len1 = (long)buf1.Size;
				var len2 = (long)buf2.Size;

				if (len1 != len2)
					return false;

				for (var i = 1; i <= len1; i++)
				{
					if (buf1[i] != buf2[i])
						return false;
				}

				return true;
			}
			else
				return left.Equals(right);//Will go here if both are double or decimal.
		}
		public static object LessThan(object left, object right)
		{
			if (left is string s1 && right is string s2)
			{
				return Strings.StrCmp(s1, s2, true) < 0;
			}
			else if (ParseNumericArgs(left, right, Keyword_LessThan, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
			{
				if (firstIsDouble)
				{
					if (secondIsDouble)
						return firstd < secondd;
					else
						return firstd < secondl;
				}
				else
				{
					if (secondIsDouble)
						return firstl < secondd;
					else
						return firstl < secondl;
				}
			}

			return DefaultObject;
		}
		public static object LessThanOrEqual(object left, object right)
		{
			if (left is string s1 && right is string s2)
			{
				return Strings.StrCmp(s1, s2, true) <= 0;
			}
			else if (ParseNumericArgs(left, right, Keyword_LessThanOrEqual, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
			{
				if (firstIsDouble)
				{
					if (secondIsDouble)
						return firstd <= secondd;
					else
						return firstd <= secondl;
				}
				else
				{
					if (secondIsDouble)
						return firstl <= secondd;
					else
						return firstl <= secondl;
				}
			}

			return DefaultObject;
		}
		public static object GreaterThan(object left, object right)
		{
			if (left is string s1 && right is string s2)
			{
				return Strings.StrCmp(s1, s2, true) > 0;
			}
			else if (ParseNumericArgs(left, right, Keyword_GreaterThan, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
			{
				if (firstIsDouble)
				{
					if (secondIsDouble)
						return firstd > secondd;
					else
						return firstd > secondl;
				}
				else
				{
					if (secondIsDouble)
						return firstl > secondd;
					else
						return firstl > secondl;
				}
			}

			return DefaultObject;
		}
		public static object GreaterThanOrEqual(object left, object right)
		{
			if (left is string s1 && right is string s2)
			{
				return Strings.StrCmp(s1, s2, true) >= 0;
			}
			else if (ParseNumericArgs(left, right, Keyword_GreaterThanOrEqual, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
			{
				if (firstIsDouble)
				{
					if (secondIsDouble)
						return firstd >= secondd;
					else
						return firstd >= secondl;
				}
				else
				{
					if (secondIsDouble)
						return firstl >= secondd;
					else
						return firstl >= secondl;
				}
			}

			return DefaultObject;
		}
		public static object ValueInequality(object left, object right)
		{
			if (left == null)
				return right != null;

			if (right == null)
				return left != null;

			_ = MatchTypes(ref left, ref right);

			if (left is string s1 && right is string s2)
				return Strings.StrCmp(s1, s2, false) != 0;
			else
				return left == null ? right != null : !left.Equals(right);//Will go here if both are double or decimal.
		}

		public static object Modulus(object left, object right)
		{
			if (ParseNumericArgs(left, right, Keyword_Modulo, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
			{
				if (firstIsDouble)
				{
					if (secondIsDouble)
						return firstd % secondd;
					else
						return firstd % secondl;
				}
				else
				{
					if (secondIsDouble)
						return firstl % secondd;
					else
						return firstl % secondl;
				}
			}

			return DefaultObject;
		}

		public static object Power(object left, object right)
		{
			if (ParseNumericArgs(left, right, Keyword_Power, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
			{
				if (firstIsDouble)
				{
					if (secondIsDouble)
						return Math.Pow(firstd, secondd);
					else
						return Math.Pow(firstd, secondl);
				}
				else
				{
					if (secondIsDouble)
						return Math.Pow(firstl, secondd);
					else
						return (long)Math.Pow(firstl, secondl);
				}
			}

			return DefaultObject;
		}

		public static object Subtract(object left, object right)
		{
			if (ParseNumericArgs(left, right, Keyword_Subtraction, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
			{
				if (firstIsDouble)
				{
					if (secondIsDouble)
						return firstd - secondd;
					else
						return firstd - secondl;
				}
				else
				{
					if (secondIsDouble)
						return firstl - secondd;
					else
						return firstl - secondl;
				}
			}

			return DefaultObject;
		}
		public static object Minus(object left, object right) => Subtract(left, right);

		public static object Multiply(object left, object right)
		{
			if (ParseNumericArgs(left, right, Keyword_Multiply, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
			{
				if (firstIsDouble)
				{
					if (secondIsDouble)
						return firstd * secondd;
					else
						return firstd * secondl;
				}
				else
				{
					if (secondIsDouble)
						return firstl * secondd;
					else
						return firstl * secondl;
				}
			}

			return DefaultObject;
		}

		public static object Divide(object left, object right)
		{
			if (ParseNumericArgs(left, right, Keyword_Divide, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
			{
				if (firstIsDouble)
				{
					if (secondIsDouble)
					{
						if (secondd == 0.0)
							return Errors.ZeroDivisionErrorOccurred("Right side operand of floating point division");

						return firstd / secondd;
					}
					else
					{
						if (secondl == 0)
							return Errors.ZeroDivisionErrorOccurred("Right side operand of floating point division");

						return firstd / secondl;
					}
				}
				else
				{
					if (secondIsDouble)
					{
						if (secondd == 0.0)
							return Errors.ZeroDivisionErrorOccurred("Right side operand of floating point division");

						return firstl / secondd;
					}
					else
					{
						if (secondl == 0)
							return Errors.ZeroDivisionErrorOccurred("Right side operand of floating point division");

						return (double)firstl / secondl;
					}
				}
			}

			return DefaultObject;
		}

		public static object Is(object left, object right)
		{
			if (left == null || right == null)
				return left == right;

			// The right operand names a class through the class itself, never through its name: `x is "Array"`
			// is a type error, and a dynamic reference has to resolve the class first (`x is %"Array"%`).
			if (right is not Any kso || kso.op == null || !kso.op.ContainsKey("Prototype"))
				return Errors.TypeErrorOccurred($"Expected Class but got {Keysharp.Builtins.Types.Type(right)}.", false);

			return Keysharp.Builtins.Types.HasBase(left, GetPropertyValue(right, "Prototype"));
		}

		internal static bool ParseNumericArgs(object left, object right, string desc, out bool firstIsDouble, out bool secondIsDouble, out double firstd, out long firstl, out double secondd, out long secondl, bool throwOnError = true)
		{
			firstIsDouble = false;
			secondIsDouble = false;
			firstd = 0.0;
			firstl = 0L;
			secondd = 0.0;
			secondl = 0L;

			if (left == null)
				return throwOnError ? (bool)Errors.UnsetErrorOccurred($"Left side operand of {desc}", false) : default;

			if (right == null)
				return throwOnError ? (bool)Errors.UnsetErrorOccurred($"Right side operand of {desc}", false) : default;

			if (left is double ld)//Check non-string types first as a hot path.
			{
				firstIsDouble = true;
				firstd = ld;
			}
			else if (left is long ll)
			{
				firstl = ll;
			}
			else if (left is bool b)
			{
				firstl = b ? 1L : 0L;
			}
			else if (left.TryParseLong(out firstl))
			{
			}
			else if (left.TryParseDouble(out firstd, true))
			{
				firstIsDouble = true;
			}
			else if (throwOnError)
			{
				return (bool)Errors.TypeErrorOccurred(left, typeof(double), false);
			}
			else
				return false;

			if (right is double rd)
			{
				secondIsDouble = true;
				secondd = rd;
			}
			else if (right is long rl)
			{
				secondl = rl;
			}
			else if (right is bool b)
			{
				secondl = b ? 1L : 0L;
			}
			else if (right.TryParseLong(out secondl))
			{
			}
			else if (right.TryParseDouble(out secondd, true))
			{
				secondIsDouble = true;
			}
			else if (throwOnError)
			{
				return (bool)Errors.TypeErrorOccurred(right, typeof(double), false);
			}
			else
				return false;

			return true;
		}

		public static object OperateTernary(bool result, ExpressionDelegate x, ExpressionDelegate y) => result ? x() : y();

		public static object MultiStatement(object arg1) => arg1;
        public static object MultiStatement(object arg1, object arg2) => arg2;
        public static object MultiStatement(object arg1, object arg2, object arg3) => arg3;
        public static object MultiStatement(object arg1, object arg2, object arg3, object arg4) => arg4;
        public static object MultiStatement(object arg1, object arg2, object arg3, object arg4, object arg5) => arg5;
        public static object MultiStatement(object arg1, object arg2, object arg3, object arg4, object arg5, object arg6) => arg6;
        public static object MultiStatement(object arg1, object arg2, object arg3, object arg4, object arg5, object arg6, object arg7) => arg7;
        public static object MultiStatement(object arg1, object arg2, object arg3, object arg4, object arg5, object arg6, object arg7, object arg8) => arg8;

        public static object MultiStatement(params object[] args) => args[ ^ 1];

        public static void InitStaticVariable(ref object variable, string name, Func<object> initFunc)
        {
            if (Script.TheScript.FlowData.initializedUserStaticVariables.Contains(name))
                return;
			Script.TheScript.FlowData.initializedUserStaticVariables.Add(name);
            variable = initFunc();
        }

		// Publishes a function's scope on the current pseudo-thread. KeysharpFunc.Call restores the previous one on return,
		// so no matching leave call is emitted.
		public static FuncScope EnterScope(FuncScope scope) => Threads.Current.executionScope = scope;

		// `%name%`. `scope` is the executing function's, or null at module level. A reference operand (`r := &x`, then `%r%`)
		// reaches its target.
		public static object DerefGet(FuncScope scope, object name)
		{
			if (Refs.DeclaresValue(name))
				return Refs.GetValueOrNull(name) ?? Errors.VarUnsetErrorOccurred(null, null);

			if (DerefKey(name) is not { } key)
				return DefaultObject;

			var found = Probe(scope, key);

			if (FuncScope.IsOwn(found))
				return found ?? Errors.VarUnsetErrorOccurred(scope, key);

			return TheScript.Vars.TryGetGlobal(TheScript.CurrentModuleType, key, out var v)
				   ? v.Get() ?? Errors.VarUnsetErrorOccurred(scope, v.DeclaredName(key))
				   : VarNotFound(key);
		}

		// The maybe read -- IsSet(%name%), %name% ?? x, %name%? -- which yields unset where DerefGet raises, except for a
		// blank name or an unset operand.
		public static object DerefGetOrNull(FuncScope scope, object name)
		{
			if (Refs.DeclaresValue(name))
				return Refs.GetValueOrNull(name);

			if (DerefKey(name) is not { } key)
				return DefaultObject;

			var found = Probe(scope, key);
			return FuncScope.IsOwn(found) ? found : TheScript.Vars.TryGetGlobal(TheScript.CurrentModuleType, key, out var v) ? v.Get() : null;
		}

		// A dynamic write target for which an error has been raised, which DerefSetFound and DerefUpdate skip.
		private static readonly object Raised = new();

		// `%name%` as the target of `:=`, `op=`, `++` or `--`, found and checked before the value is evaluated, as AutoHotkey
		// resolves it: a reference, the function's own name, a global's holder or property, or Raised.
		public static object DerefTarget(FuncScope scope, object name)
		{
			if (Refs.DeclaresValue(name))
				return name;

			return DerefKey(name) is { } key && TryFindWrite(scope, key, VarUsage.Assign, out var v) ? v.Exists ? v.Target : key : Raised;
		}

		// `%name% ??= value`: true when the target has no value and takes the write, with `target` what DerefSetFound assigns,
		// else `current` is the expression's value. A target with a value is not checked, as AutoHotkey checks only an
		// assignment it makes.
		public static bool DerefGetForWrite(FuncScope scope, object name, out object current, out object target)
		{
			target = Raised;
			current = DefaultObject;

			if (Refs.DeclaresValue(name))
			{
				target = name;
				return (current = Refs.GetValueOrNull(name)) == null;
			}

			if (DerefKey(name) is not { } key)
				return false;

			var found = Probe(scope, key);

			if ((FuncScope.IsOwn(found) ? found : TryFindForWrite(scope, found, key, out var v) ? v.Get() : null) is { } value)
			{
				current = value;
				return false;
			}

			return (target = DerefTarget(scope, key)) != Raised;
		}

		// Assigns what DerefTarget or DerefGetForWrite found, returning the value.
		public static object DerefSetFound(FuncScope scope, object target, object value)
		{
			if (target is string key)
				_ = scope.Write(key, value);
			else if (ScriptVar.FromTarget(target) is { Exists: true } v)
				v.Set(value);
			else if (target != Raised)
				_ = Refs.SetValue(target, value);

			return value;
		}

		// `%name% op= value`, `++%name%` and `%name%++` on what DerefTarget found, yielding the new value, or the old one for a
		// postfix operator.
		public static object DerefUpdate(FuncScope scope, object target, Func<object, object, object> op, object value, bool postfix)
		{
			if (target == Raised)
				return DefaultObject;

			var old = target is string key ? scope.Read(key) : ScriptVar.FromTarget(target) is { Exists: true } v ? v.Get() : Refs.GetValueOrNull(target);
			var result = DerefSetFound(scope, target, op(old, value));
			return postfix ? old : result;
		}

		// `&%name%`: a reference bound to the variable the name finds now, found and validated as a write finds it
		// (VARREF_REF), so neither a later change to the name nor the module it is used from redirects it.
		public static object DerefRef(FuncScope scope, object name)
		{
			if (Refs.DeclaresValue(name))
				return Misc.MakeVarRef(() => Refs.GetValueOrNull(name), value => Refs.SetValue(name, value));

			if (DerefKey(name) is not { } key || !TryFindWrite(scope, key, VarUsage.Reference, out var v))
				return DefaultObject;

			return v.Exists ? v.MakeRef() : Misc.MakeVarRef(() => scope.Read(key), value => scope.Write(key, value));
		}

		// What a read of a name finds among the function's own variables, or FuncScope.Global at module level.
		private static object Probe(FuncScope scope, string key) => scope == null ? FuncScope.Global : scope.Read(key);

		// What a write to `key` reaches, or false once it has raised for a name no write reaches: a variable of the function,
		// with `v` empty, or a variable beyond the function in `v`.
		private static bool TryFindWrite(FuncScope scope, string key, VarUsage usage, out ScriptVar v)
		{
			var found = Probe(scope, key);

			if (FuncScope.IsOwn(found))
			{
				if (scope.TryGetDeclaration(key, out var declared))
				{
					v = default;

					if (declared.Kind != VarKind.Constant)
						return true;

					_ = Errors.VarReadOnlyErrorOccurred(Errors.ConstantKind(found), declared.Name, usage);
					return false;
				}

				// A wildcard member the function holds no write for is written as a name it does not hold.
				found = scope.Unheld;
			}

			if (TryFindForWrite(scope, found, key, out v))
				return v.RequireWritable(usage, key);

			_ = DerefNotFound(scope, found, key);
			return false;
		}

		// The name a dynamic reference looks up, or null once it has raised for an unset or blank name.
		private static string DerefKey(object name)
		{
			if (name == null)
				_ = Errors.UnsetErrorOccurred("Operand of dereference");
			else if (ForceString(name) is { Length: > 0 } key)
				return key;
			else
				_ = Errors.ErrorOccurred("This dynamic variable is blank.");

			return null;
		}

		// A write beyond the function's own variables finds, at module level, the module's variable or a built-in one;
		// from a function, any global once declared, else only a built-in variable.
		private static bool TryFindForWrite(FuncScope scope, object found, string key, out ScriptVar v)
		{
			var vars = TheScript.Vars;
			return scope == null ? vars.TryGetModuleWriteTarget(TheScript.CurrentModuleType, key, out v)
				   : ReferenceEquals(found, FuncScope.Global) ? vars.TryGetGlobal(TheScript.CurrentModuleType, key, out v)
				   : vars.TryGetBuiltinVar(key, out v);
		}

		private static object DerefNotFound(FuncScope scope, object found, string key) =>
			scope != null && ReferenceEquals(found, FuncScope.Undeclared) && TheScript.Vars.TryGetGlobal(TheScript.CurrentModuleType, key, out _)
			? Errors.ErrorOccurred("This dynamic assignment requires a \"global\" declaration.", null, extra: key)
			: VarNotFound(key);

		private static object VarNotFound(string key) => Errors.ErrorOccurred("Variable not found.", null, extra: key);


		// Unary operators
		public static object Plus(object right) => right;

		public static object Minus(object right)
		{
			if (right == null)
				return Errors.UnsetErrorOccurred($"Right side operand of subtraction or minus");

			if (right is double rd)//Check non-string types first as a hot path.
				return rd == 0d ? rd : -rd;
			else if (right is long rl)
				return -rl;
			else if (right.TryParseLong(out long l))
				return -l;
			else if (right.TryParseDouble(out double d, true))
				return d == 0d ? d : -d;
			else
				return Errors.TypeErrorOccurred(right, typeof(double));
		}

		public static object LogicalNot(object right) => !IfTest(right);

		public static object BitwiseNot(object right)
		{
			if (right == null)
				return Errors.UnsetErrorOccurred($"Right side operand of bitwise not");

			if (right is double)
				return Errors.TypeErrorOccurred(right, typeof(long));

			if (right.TryParseLong(out long l))
				return ~l;

			return Errors.TypeErrorOccurred(right, typeof(long));
		}

		public static int OperateZero(object expression) => 0;


		// Is methods

		internal static bool IsFloat(object obj) =>
		obj is double/* ||
        obj is float ||
        obj is decimal*/;

		internal static bool IsInteger(object obj) =>
		obj is long
		/*  ||
		    obj is int ||
		    obj is ulong ||
		    obj is uint ||
		    obj is short ||
		    obj is ushort ||
		    obj is char ||
		    obj is sbyte ||
		    obj is byte
		*/;

		internal static bool IsFloatType(Type type) => type == typeof(double);
		internal static bool IsIntegerType(Type type) => type == typeof(long);
		internal static bool IsNumeric(Type type) =>
		IsIntegerType(type)
		|| IsFloatType(type)
		/*
		    || type == typeof(int)
		    || type == typeof(uint)
		    || type == typeof(ulong)
		    || type == typeof(float)
		    || type == typeof(decimal)
		    || type == typeof(byte)
		    || type == typeof(sbyte)*/
		;

		internal static bool IsNumeric(object value) => value != null&& IsNumeric(value.GetType());

		public enum Operator
		{
			Add,
			Subtract,
			Multiply,
			Divide,
			Modulus,
			Assign,
			IdentityInequality,
			IdentityEquality,
			ValueEquality,
			BitwiseOr,
			BitwiseAnd,
			BooleanOr,
			BooleanAnd,
			RegEx,
			LessThan,
			LessThanOrEqual,
			GreaterThan,
			GreaterThanOrEqual,

			Increment,
			Decrement,

			Minus,
			LogicalNot,
			BitwiseNot,
			Address,
			Dereference,

			Power,
			FloorDivide,
			BitShiftRight,
			BitShiftLeft,
			LogicalBitShiftRight,
			BitwiseXor,
			ValueInequality,
			Concat,

			LogicalNotEx,

			TernaryA,
			TernaryB,

			Is,
			NullCoalesce,
		};

		public delegate object ExpressionDelegate();
	}
}
