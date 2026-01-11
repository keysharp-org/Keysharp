namespace Keysharp.Scripting
{
	public partial class Script
	{
		const string Addition = "addition";
		const string Divide = "division";
		const string Multiply = "multiplication";
		const string Subtraction = "subtraction";
		const string ArLeftShift = "arithmetic left shift";
		const string ArRightShift = "arithmetic right shift";
		const string LogicalRightShift = "logical right shift";
		const string BitwiseAnd = "bitwise and";
		const string BitwiseOr = "bitwise or";
		const string BitwiseXor = "bitwise xor";
		const string LessThan = "less than";
		const string LessThanOrEqual = "less than or equal";
		const string GreaterThan = "greater than";
		const string GreaterThanOrEqual = "greater than or equal";
		const string Modulo = "modulo";
		const string Power = "power";
		const string Between = "between";
		const string In = "in";
		const string Contains = "contains";
		const string Is = "is";
		const char Delimiter = ',';
		const string And = " and ";
		const string Integer = "integer";
		const string Float = "float";
		const string Number = "number";
		const string Digit = "digit";
		const string Xdigit = "xdigit";
		const string Alpha = "alpha";
		const string Upper = "upper";
		const string Lower = "lower";
		const string Alnum = "alnum";
		const string Space = "space";
		const string Time = "time";

		public static bool IfLegacy(object subject, string op, string test, bool not = false)
		{
			var variable = ForceString(subject);
			var varspan = variable.AsSpan();
			var ret = false;

			switch (op)
			{
				case Between:
				{
					if (subject == null)
						return (bool)Errors.UnsetErrorOccurred($"Left side operand of between", false);

					if (test == null)
						return (bool)Errors.UnsetErrorOccurred($"Right side operand of between", false);

						var z = test.IndexOf(And, StringComparison.OrdinalIgnoreCase);

						if (z == -1)
							z = variable.Length;

						if (double.TryParse(test.AsSpan(0, z), out var low) && double.TryParse(test.AsSpan(z + And.Length), out var high))
						{
							var d = ForceDouble(subject);
							ret = d >= low && d <= high;
						}
						else if (subject is string s)
						{
							ret = string.Compare(test.Substring(0, z), s) < 0 && string.Compare(s, test.Substring(z + And.Length)) < 0;
						}
					}
					break;

				case In:
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

				case Contains:
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

				case Is:
					if (test == null)
						return subject == null;

					test = test.ToLowerInvariant();

					//Put common cases first.
					switch (test)
					{
						case Integer:
							ret = IsInteger(subject);
							goto done;

						case Float:
							ret = IsFloat(subject);
							goto done;

						case Number:
							ret = IsInteger(subject) || IsFloat(subject);
							goto done;

						case "string":
							ret = subject is string;
							goto done;

						case "unset":
						case "null":
							ret = subject == null;
							goto done;
					}

					var alias = Keywords.TypeNameAliases.FirstOrDefault(kvp => kvp.Value.Equals(test, StringComparison.OrdinalIgnoreCase)).Key;
					if (alias != null)
						test = alias;

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

						if (Parser.IsTypeOrBase(type, test))
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

			// Build "Outer.Inner" from declaring types (namespaces are not included here).
			var names = new List<string>();
			for (var cur = t; cur != null && cur != script.ProgramType; cur = cur.DeclaringType)
				names.Add(cur.Name);
			names.Reverse();
			return string.Join('.', names);
		}


		public static BoolResult IfTest(object result) => new (ForceBool(result), result);

		public static object PrefixIncDec(Operator op, object left, object val) => Operate(op, left, val);

		public static object PostfixIncDecIndex(object obj, object index, object val)
		{
			var orig = Index(obj, index);
			_ = SetObject(obj, index, Operate(Operator.Add, orig, val));
			return orig;
		}

		public static object PostfixIncDecProp(object obj, object prop, object val)
		{
			var orig = GetPropertyValue(obj, prop);
			var newval = Operate(Operator.Add, orig, val);
			_ = SetPropertyValue(obj, prop, newval);
			return orig;
		}

		public static object Operate(Operator op, object left, object right)
		{
			switch (op)
			{
				case Operator.Add:
				{
					if (ParseNumericArgs(left, right, Addition, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
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

					return DefaultErrorObject;
				}

				case Operator.BitShiftLeft:
				{
					if (ParseNumericArgs(left, right, ArLeftShift, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
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

						return DefaultErrorObject;
					}

				case Operator.BitShiftRight:
				{
					if (ParseNumericArgs(left, right, ArRightShift, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
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

						return DefaultErrorObject;
					}

				case Operator.LogicalBitShiftRight:
				{
					if (ParseNumericArgs(left, right, LogicalRightShift, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
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

						return DefaultErrorObject;
					}

				case Operator.BitwiseAnd:
				{
					if (ParseNumericArgs(left, right, BitwiseAnd, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
					{
						if (firstIsDouble)
								return Errors.TypeErrorOccurred(left, typeof(long));

							if (secondIsDouble)
								return Errors.TypeErrorOccurred(right, typeof(long));

							return firstl & secondl;
						}

						return DefaultErrorObject;
					}

				case Operator.BitwiseOr:
				{
					if (ParseNumericArgs(left, right, BitwiseOr, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
					{
						if (firstIsDouble)
							return Errors.TypeErrorOccurred(left, typeof(long));

						if (secondIsDouble)
							return Errors.TypeErrorOccurred(right, typeof(long));

							return firstl | secondl;
					}

					return DefaultErrorObject;
				}

				case Operator.BitwiseXor:
				{
					if (ParseNumericArgs(left, right, BitwiseXor, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
					{
						if (firstIsDouble)
								return Errors.TypeErrorOccurred(left, typeof(long));

							if (secondIsDouble)
								return Errors.TypeErrorOccurred(right, typeof(long));

							return firstl ^ secondl;
						}

						return DefaultErrorObject;
					}

				case Operator.BooleanAnd:
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

				case Operator.BooleanOr:
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

				case Operator.Concat:
				{
					//Do not check the left side for null, AHK allows it.
					if (right == null)
						return (bool)Errors.UnsetErrorOccurred($"Right side operand of concat", false);

					// Guard agains accidental function object concatenation (likely used function call statement in an expression context)
					if (left is FuncObj)
						return Errors.TypeErrorOccurred(left, typeof(string));

					return string.Concat(ForceString(left), ForceString(right));
				}

				case Operator.RegEx:
				{
					if (left == null)
						return (bool)Errors.UnsetErrorOccurred($"Left side operand of regular expression", false);

					if (right == null)
						return (bool)Errors.UnsetErrorOccurred($"Right side operand of regular expression", false);

                        VarRef outvar = new VarRef(null);
						return RegEx.RegExMatch(ForceString(left), ForceString(right), outvar, 1);
					}

				case Operator.FloorDivide:
				{
					if (ParseNumericArgs(left, right, BitwiseOr, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
					{
						if (firstIsDouble)
								return Errors.TypeErrorOccurred(left, typeof(long));

							if (secondIsDouble)
								return Errors.TypeErrorOccurred(right, typeof(long));

							if (secondl == 0L)
								return Errors.ZeroDivisionErrorOccurred("Right side operand of floor divide");

							return firstl / secondl;
						}

						return DefaultErrorObject;
					}

				case Operator.IdentityInequality:
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

				case Operator.IdentityEquality://This is for a double equal sign in a conditional, and uses case sensitive comparison for strings.
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

				case Operator.ValueEquality://This is for a single equal sign in a conditional, and uses the case insensitive comparison type for strings.
					{
						if (left == null)
							return right == null;

						if (right == null)
							return left == null;

						_ = MatchTypes(ref left, ref right);

						if (left is string s1 && right is string s2)
							return Strings.StrCmp(s1, s2, false) == 0;
						else if (left is Core.Array al1 && right is Core.Array al2)
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
						else if (left is Core.Buffer buf1 && right is Core.Buffer buf2)
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

				case Operator.LessThan:
				{
					if (left is string s1 && right is string s2)
					{
						return Strings.StrCmp(s1, s2, true) < 0;
					}
					else if (ParseNumericArgs(left, right, LessThan, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
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

					return DefaultErrorObject;
				}

				case Operator.LessThanOrEqual:
				{
					if (left is string s1 && right is string s2)
					{
						return Strings.StrCmp(s1, s2, true) <= 0;
					}
					else if (ParseNumericArgs(left, right, LessThanOrEqual, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
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

					return DefaultErrorObject;
				}

				case Operator.GreaterThan:
				{
					if (left is string s1 && right is string s2)
					{
						return Strings.StrCmp(s1, s2, true) > 0;
					}
					else if (ParseNumericArgs(left, right, GreaterThan, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
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

					return DefaultErrorObject;
				}

				case Operator.GreaterThanOrEqual:
				{
					if (left is string s1 && right is string s2)
					{
						return Strings.StrCmp(s1, s2, true) >= 0;
					}
					else if (ParseNumericArgs(left, right, GreaterThanOrEqual, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
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

					return DefaultErrorObject;
				}

				case Operator.ValueInequality://This is for != or <> in a conditional.
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

				case Operator.Modulus:
				{
					if (ParseNumericArgs(left, right, Modulo, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
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

					return DefaultErrorObject;
				}

				case Operator.Power:
				{
					if (ParseNumericArgs(left, right, Power, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
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

					return DefaultErrorObject;
				}

				case Operator.Minus:
				case Operator.Subtract:
				{
					if (ParseNumericArgs(left, right, Subtraction, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
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

					return DefaultErrorObject;
				}

				case Operator.Multiply:
				{
					if (ParseNumericArgs(left, right, Multiply, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
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

					return DefaultErrorObject;
				}

				case Operator.Divide:
				{
					if (ParseNumericArgs(left, right, Divide, out var firstIsDouble, out var secondIsDouble, out var firstd, out var firstl, out var secondd, out var secondl))
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

						return DefaultErrorObject;
					}

				case Operator.Is:
				{ 
					if (left == null || right == null)
						return left == right;
					return IfLegacy(left, "is", ForceString(right));
				}

				default:
					return Errors.ValueErrorOccurred($"Operator {op} cannot be applied to: {left} and {right}");
			}
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
			else if (left.ParseLong(out firstl, false, false))
			{
			}
			else if (left.ParseDouble(out firstd, false, true))
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
			else if (right.ParseLong(out secondl, false, false))
			{
			}
			else if (right.ParseDouble(out secondd, false, true))
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

        public static object OperateUnary(Operator op, object right)
		{
			switch (op)
			{
				case Operator.Add:
					return right;
				case Operator.Minus:
				case Operator.Subtract:
				{
						if (right == null)
							return Errors.UnsetErrorOccurred($"Right side operand of subtraction or minus");

						if (right is double rd)//Check non-string types first as a hot path.
							return rd == 0d ? rd : -rd;
						else if (right is long rl)
							return -rl;
						else if (right.ParseLong(out long l, false, false))
							return -l;
						else if (right.ParseDouble(out double d, false, true))
							return d == 0d ? d : -d;
						else
							return Errors.TypeErrorOccurred(right, typeof(double));
					}

				case Operator.LogicalNot:
				case Operator.LogicalNotEx:
					return !IfTest(right);

				case Operator.BitwiseNot:
				{
					if (right == null)
							return Errors.UnsetErrorOccurred($"Right side operand of bitwise not");

						if (right is double)
							return Errors.TypeErrorOccurred(right, typeof(long));

						if (right.ParseLong(out long l, false, false))
							return ~l;

						return Errors.TypeErrorOccurred(right, typeof(long));
					}

				//Not supporting references at this time.
				//case Operator.Dereference:
				// TODO: dereference operator
				//return null;
				//case Operator.BitwiseAnd:
				//return GCHandle.Alloc(right, GCHandleType.Pinned).AddrOfPinnedObject().ToInt64();//This seems almost certainly wrong, and would need to be freed elsewhere.

				default:
					return Errors.ValueErrorOccurred($"Operator {op} cannot be applied to: {right}");
			}
		}

		public static int OperateZero(object expression) => 0;

		public static object OrMaybe(object left, object right) => Types.IsSet(left) == 1L ? left : right;

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