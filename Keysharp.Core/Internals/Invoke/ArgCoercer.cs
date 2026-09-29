using Keysharp.Builtins;
using System.Linq.Expressions;

namespace Keysharp.Internals.Invoke
{
	/// <summary>
	/// The single conversion policy for a value crossing between script and a <em>typed</em> CLR parameter,
	/// property or field.
	///
	/// <para>Before this existed, the dynamic-invoke path unboxed straight into the declared type
	/// (<c>Expression.Convert(object, T)</c>). Because AutoHotkey has exactly two numeric types, a script
	/// passing <c>true</c> (an <c>Int64</c>) to a <c>bool</c> parameter, or <c>10.5</c> to a <c>long</c> one,
	/// raised an <see cref="InvalidCastException"/> from inside the compiled core — which is not a
	/// <c>KeysharpException</c>, so no script <c>try/catch</c> could intercept it and the process died. See the
	/// comments on <c>WinEvents.OnEvent</c> and <c>KeysharpThread</c>, both of which were written around it.</para>
	///
	/// <para>Nothing is reimplemented here. Each kind delegates to the converter the rest of the runtime already
	/// uses, which is also what decides whether it can fail:</para>
	/// <list type="bullet">
	/// <item>numeric — <see cref="ObjectExtensions.ToLong"/> / <see cref="ObjectExtensions.ToDouble"/>: a numeric
	/// string converts (<c>"1"</c> to 1, matching <c>"1" == 1</c>), a Float truncates toward zero, and anything
	/// else raises a <see cref="TypeError"/>, the same error <c>1 + "abc"</c> and <c>Integer("abc")</c> produce.</item>
	/// <item><c>bool</c> — <see cref="Script.ForceBool"/>: AutoHotkey truthiness, total. A non-empty non-numeric
	/// string is <c>true</c>, so this never raises for a value; only an unset one does.</item>
	/// <item><c>string</c> — <see cref="ObjectExtensions.As"/>: total, and honors a script class's own
	/// <c>ToString</c> override. Unset becomes <c>""</c>.</item>
	/// <item>enum targets — <see cref="Enum.ToObject(Type, long)"/> over the same numeric conversion, since a
	/// script has only the Integer to name a member (or a flag combination) with.</item>
	/// <item><c>byte[]</c> — a <see cref="Keysharp.Builtins.Buffer"/> is copied to a new array; byte spans view its
	/// storage directly for the duration of the call.</item>
	/// <item>reference targets — a checked cast that raises a <see cref="TypeError"/> naming both types instead
	/// of an uncatchable <see cref="InvalidCastException"/>. Unset passes through as null.</item>
	/// </list>
	///
	/// <para>A conversion error the script continues stops the call, as it stops an AutoHotkey built-in: each
	/// conversion returns false for it, and the member does not run.</para>
	///
	/// <para>Ordinary dispatch is deliberately narrow: only the types <see cref="KindOf"/> claims are intercepted.
	/// Anything else keeps its previous raw-unbox behavior. An explicitly marked inline-C# boundary additionally
	/// unwraps a managed proxy when its payload fits the declared target.</para>
	/// </summary>
#if !INTERNALDEBUG
	[DebuggerStepThrough]
#endif
	internal static class ArgCoercer
	{
		/// <summary>
		/// The conversion rule for a target type. Everything from <see cref="Int"/> onward is a type a script has
		/// no equivalent for and must be widened on the way back out — see <see cref="IsNarrow"/>, which depends on
		/// that ordering.
		/// </summary>
		internal enum Kind { None, Long, Double, Bool, Str, Cast, Enum, Int, UInt, Short, UShort, Byte, SByte, ULong, NInt, NUInt, Single }

		/// <summary>The conversion rule for <paramref name="t"/>, or <see cref="Kind.None"/> to leave it alone.</summary>
		internal static Kind KindOf(Type t)
		{
			// An enum reports its underlying integral type, so it has to be claimed before the switch below picks
			// the kind for that type and unboxes an Int32 straight into an Int32-BACKED target, which is not the
			// same thing. It is not narrow: an enum leaves for a script as a wrapped value, never widened to
			// Integer, so it sorts before Kind.Int (see IsNarrow) and CanLeakClrValue keeps claiming it.
			if (t.IsEnum)
				return Kind.Enum;

			// Byref/pointer types report as non-value types, so they too would be claimed for a kind that cannot
			// represent them.
			if (t.IsByRef || t.IsPointer || t.IsFunctionPointer)
				return Kind.None;

			switch (Type.GetTypeCode(t))
			{
				case TypeCode.Int64: return Kind.Long;
				case TypeCode.Double: return Kind.Double;
				case TypeCode.Boolean: return Kind.Bool;
				case TypeCode.String: return Kind.Str;
				case TypeCode.Int32: return Kind.Int;
				case TypeCode.UInt32: return Kind.UInt;
				case TypeCode.Int16: return Kind.Short;
				case TypeCode.UInt16: return Kind.UShort;
				case TypeCode.Byte: return Kind.Byte;
				case TypeCode.SByte: return Kind.SByte;
				case TypeCode.UInt64: return Kind.ULong;
				case TypeCode.Single: return Kind.Single;

				case TypeCode.Object:
					if (t == typeof(nint)) return Kind.NInt;

					if (t == typeof(nuint)) return Kind.NUInt;

					// `object` needs no conversion at an ordinary script boundary, and `object[]` is the packed
					// variadic slot which the caller has already built and must hand over untouched. Everything else
					// that is a reference (Map, Array, Any-derived, …) gets the checked cast; null still passes through
					// it, which optional parameters such as `Ks.Mail(…, Map options = null)` depend on.
					return t == typeof(object) || t == typeof(object[]) || t.IsValueType ? Kind.None : Kind.Cast;

				default: return Kind.None;//Char, Decimal, DateTime, ...
			}
		}

		/// <summary>
		/// True for a kind AutoHotkey has no type for, which <see cref="NormalizeScalar"/> and
		/// <see cref="NormalizeReturn"/> must widen to Integer or Float.
		/// </summary>
		internal static bool IsNarrow(Kind k) => k >= Kind.Int;

		/// <summary>The <see cref="Kind.Cast"/> conversion used by ordinary script calls.</summary>
		internal static bool TryCoerceCast(object value, Type target, out object result)
		{
			if (target == typeof(byte[]))
			{
				if (value is Ks.Clr.ManagedInstance mi)
					value = mi._instance;

				if (value is Keysharp.Builtins.Buffer
					|| value is Any any && Reflections.TryGetPtrProperty(any, out _) && Reflections.TryGetSizeProperty(any, out _))
				{
					result = Conversions.ToByteArray(value);
					return true;
				}
			}

			if (value == null || target.IsInstanceOfType(value))
			{
				result = value;
				return true;
			}

			_ = Errors.TypeErrorOccurred(value, target);
			result = null;
			return false;
		}

		internal static bool IsByteSpan(Type type) => type == typeof(Span<byte>) || type == typeof(ReadOnlySpan<byte>);

		internal static unsafe bool TryCoerceByteSpan(object value, out Span<byte> result)
		{
			if (value is Ks.Clr.ManagedInstance mi)
				value = mi._instance;

			result = default;

			if (value is Keysharp.Builtins.Buffer buffer)
				result = buffer.AsSpan();
			else if (value is byte[] bytes)
				result = bytes;
			else if (value is Any any && Reflections.TryGetPtrProperty(any, out var ptr) && Reflections.TryGetSizeProperty(any, out var size))
			{
				if (size < 0 || size > int.MaxValue)
				{
					_ = Errors.ValueErrorOccurred($"Byte source Size must be between 0 and {int.MaxValue}.", size);
					return false;
				}

				result = new Span<byte>((void*)(nint)ptr, (int)size);
			}
			else
			{
				_ = Errors.TypeErrorOccurred(value, typeof(Span<byte>));
				return false;
			}

			return true;
		}

		internal static bool TryCoerceReadOnlyByteSpan(object value, out ReadOnlySpan<byte> result)
		{
			var ok = TryCoerceByteSpan(value, out var span);
			result = span;
			return ok;
		}

		/// <summary>
		/// The <see cref="Kind.Enum"/> conversion. AutoHotkey has no enum type, so a script names a member of one
		/// in exactly two ways, and both arrive here: as an Integer — including one built by flag arithmetic, which
		/// need not be a declared member — or as the member itself fetched through <c>Ks.Clr</c>, which arrives as
		/// a proxy or a boxed enum. <see cref="Enum.ToObject(Type, long)"/> takes all of them; it converts by value
		/// and does not require the result to be named, which is what a flag combination depends on.
		/// <para>Reflection would accept a bare integral only when it can widen it to the underlying type, and
		/// script Integers are Int64 while nearly every .NET enum is Int32-backed — so without this the common case
		/// is the one that fails.</para>
		/// </summary>
		internal static bool TryCoerceEnum(object value, Type target, out object result)
		{
			// A proxy can only have been meant as its payload here. This is not the boundary-only unwrapping
			// TryCoerceBoundaryCast does: an enum parameter, unlike an `object` or reference one, has no reading in
			// which a ManagedInstance is itself the argument.
			if (value is Ks.Clr.ManagedInstance mi)
				value = mi._instance;

			if (target.IsInstanceOfType(value))
			{
				result = value;
				return true;
			}

			// A boxed enum of some other type converts by value, like the Integer it stands for. Its own underlying
			// type is the only lossless stop on the way there: ToLong would fall through to ToString(), which
			// yields the member NAME for a named value and the number only for an unnamed one.
			if (value is Enum e)
			{
				result = Enum.ToObject(target, Convert.ChangeType(e, Enum.GetUnderlyingType(e.GetType()), CultureInfo.InvariantCulture));
				return true;
			}

			// Everything else takes the numeric policy the integral kinds take: a numeric string converts, a Float
			// truncates, and anything else raises the TypeError `Integer("abc")` raises. Out-of-range bits are
			// dropped rather than throwing, matching the unchecked casts those kinds use.
			var ok = TryLong(value, out var l);
			result = Enum.ToObject(target, l);
			return ok;
		}

		// The scalar conversions shared by the expression trees and TryCoerceValue. False means the script continued
		// the conversion's error, and the call they feed must not run.
		private static bool TryLong(object value, out long result) => value.CoerceLong(out result);

		private static bool TryDouble(object value, out double result) => value.CoerceDouble(out result);

		private static bool TryText(object value, out string result) => value.CoerceString(out result);

		// ForceBool raises only for an unset value, so returning from it with one means the script continued that error.
		private static bool TryBool(object value, out bool result)
		{
			result = ForceBool(value);
			return value != null;
		}

		/// <summary>Unwraps managed proxies at an explicit CLR boundary.</summary>
		internal static bool TryCoerceBoundaryCast(object value, Type target, out object result)
		{
			// ManagedInstance represents its payload even for an object slot. ManagedType remains a script object for
			// object, matching Ks.Clr, and unwraps only for Type-compatible targets.
			if (value is Ks.Clr.ManagedInstance mi && (target == typeof(object) || target.IsInstanceOfType(mi._instance)))
				result = mi._instance;
			else if (value == null || target.IsInstanceOfType(value))
				result = value;
			// Ks.Task is the script's face on a CLR Task, so an inline member declaring a Task parameter must
			// receive the task itself. `object` deliberately keeps the wrapper -- the same call ManagedInstance
			// makes above is reversed here, because a Ks.Task is itself a script object while a ManagedInstance is only
			// a view of one.
			else if (value is Ks.KeysharpTask kt && target.IsInstanceOfType(kt.Underlying))
				result = kt.Underlying;
			else if (value is Ks.Clr.ManagedType mt && target.IsInstanceOfType(mt._type))
				result = mt._type;
			else
			{
				_ = Errors.TypeErrorOccurred(value, target);
				result = null;
				return false;
			}

			return true;
		}

		/// <summary>
		/// Runtime counterpart of <see cref="ArgumentSteps.Coerce"/>, for the paths that never build an expression
		/// tree: the Clr boundary, the reserved-variable (A_*) setters, and every reflection-based property setter.
		/// Gives a value boxed as exactly <paramref name="target"/>. False when the script continued the conversion's
		/// error, so the member must not be given <paramref name="result"/>, which is then the target's empty value.
		/// </summary>
		internal static bool TryCoerceValue(object value, Type target, bool boundary, out object result)
		{
			var kind = KindOf(target);

			if (boundary && NeedsBoundaryCast(target, kind))
				return TryCoerceBoundaryCast(value, target, out result);

			var ok = true;

			switch (kind)
			{
				case Kind.Long: result = AsLong(); break;
				case Kind.Double: result = AsDouble(); break;
				case Kind.Bool: ok = TryBool(value, out var b); result = b; break;
				case Kind.Str: ok = TryText(value, out var s); result = s; break;
				case Kind.Cast: return TryCoerceCast(value, target, out result);
				case Kind.Enum: return TryCoerceEnum(value, target, out result);
				case Kind.Int: result = unchecked((int)AsLong()); break;
				case Kind.UInt: result = unchecked((uint)AsLong()); break;
				case Kind.Short: result = unchecked((short)AsLong()); break;
				case Kind.UShort: result = unchecked((ushort)AsLong()); break;
				case Kind.Byte: result = unchecked((byte)AsLong()); break;
				case Kind.SByte: result = unchecked((sbyte)AsLong()); break;
				case Kind.ULong: result = unchecked((ulong)AsLong()); break;
				case Kind.NInt: result = unchecked((nint)AsLong()); break;
				case Kind.NUInt: result = unchecked((nuint)AsLong()); break;
				case Kind.Single: result = (float)AsDouble(); break;
				default: result = value; break;
			}

			return ok;

			long AsLong()
			{
				ok = TryLong(value, out var l);
				return l;
			}

			double AsDouble()
			{
				ok = TryDouble(value, out var d);
				return d;
			}
		}

		private static bool NeedsBoundaryCast(Type target, Kind kind) =>
			kind == Kind.Cast && target != typeof(byte[])
			// object[] is Kind.None because a packed params slot must stay untouched. CompileCore skips that slot,
			// leaving a non-variadic object[] free to use the normal CLR-boundary conversion here.
			|| kind == Kind.None && (target == typeof(object) || target == typeof(object[]) || target.IsValueType);

		/// <summary>
		/// Whether a value for <paramref name="target"/> needs conversion: script scalar conversion, and at a CLR
		/// boundary also proxy unwrapping.
		/// </summary>
		internal static bool NeedsCoercion(Type target, bool boundary) => TryConverter(target, boundary) != null;

		// The Try conversion a compiled argument for target takes, or null when the argument is only unboxed or cast.
		// Its last parameter is the out slot its temporary fills; one with three also takes the target type.
		private static MethodInfo TryConverter(Type target, bool boundary)
		{
			if (boundary && target == typeof(Span<byte>))
				return tryCoerceByteSpanMethod;

			if (boundary && target == typeof(ReadOnlySpan<byte>))
				return tryCoerceReadOnlyByteSpanMethod;

			var kind = KindOf(target);

			if (boundary && NeedsBoundaryCast(target, kind))
				return tryCoerceBoundaryCastMethod;

			return kind switch
			{
				Kind.None => null,
				Kind.Double or Kind.Single => tryDoubleMethod,
				Kind.Bool => tryBoolMethod,
				Kind.Str => tryTextMethod,
				Kind.Cast => tryCoerceCastMethod,
				Kind.Enum => tryCoerceEnumMethod,
				_ => tryLongMethod,//Long and the narrow integral kinds, which the argument narrows unchecked.
			};
		}

		/// <summary>
		/// The statements a compiled call runs before it: its arguments evaluated into temporaries, in order. A
		/// conversion error the script continued leaves through the stop label with the stop value before the member
		/// runs, and with nothing on the evaluation stack to jump over.
		/// </summary>
		internal sealed class ArgumentSteps
		{
			private readonly List<ParameterExpression> variables = [];
			private readonly List<Expression> steps = [];
			private readonly LabelTarget stop;
			private readonly Expression stopValue;

			/// <param name="stopValue">What the block yields when a conversion stops it; null for a block without a value.</param>
			internal ArgumentSteps(Expression stopValue)
			{
				this.stopValue = stopValue;
				stop = Expression.Label(stopValue?.Type ?? typeof(void), "stop");
			}

			/// <summary>
			/// Converts <paramref name="value"/> (an <c>object</c> expression) to <paramref name="target"/> in a step of
			/// its own, and returns the argument that step leaves.
			/// </summary>
			internal Expression Coerce(Expression value, Type target, bool boundary)
			{
				var converter = TryConverter(target, boundary);

				if (converter == null)
					return Hold(Expression.Convert(value, target));

				var ps = converter.GetParameters();
				var temp = Expression.Variable(ps[^1].ParameterType.GetElementType()!);
				variables.Add(temp);
				var converted = ps.Length == 3
					? Expression.Call(converter, value, Expression.Constant(target, typeof(Type)), temp)
					: Expression.Call(converter, value, temp);
				steps.Add(Expression.IfThen(Expression.Not(converted), Expression.Return(stop, stopValue)));

				return temp.Type == target ? (Expression)temp
					// There is no Int64 -> UIntPtr coercion operator, so nuint alone needs the ulong stepping stone.
					: target == typeof(nuint) ? Expression.Convert(Expression.Convert(temp, typeof(ulong)), target)
					: Expression.Convert(temp, target);//Expression.Convert is unchecked.
			}

			/// <summary>Evaluates an argument needing no conversion in a step of its own, keeping the arguments' order.</summary>
			internal Expression Hold(Expression value)
			{
				var temp = Expression.Variable(value.Type);
				variables.Add(temp);
				steps.Add(Expression.Assign(temp, value));
				return temp;
			}

			/// <summary>Runs the steps and then <paramref name="body"/>, which the stop value replaces when a conversion stops the call.</summary>
			internal Expression Wrap(Expression body)
			{
				steps.Add(body);
				var block = Expression.Block(variables, steps);

				// A jump carrying a value may only leave for a label that encloses it, so the label wraps the whole block.
				return stop.Type == typeof(void) ? Expression.Block(block, Expression.Label(stop)) : Expression.Label(stop, block);
			}
		}

		/// <summary>
		/// Boxes a value on its way back to script. AutoHotkey has only Integer and Float, and the runtime's hot
		/// paths test for exactly <c>long</c>/<c>double</c>/<c>bool</c> (see <c>Script.ParseNumericArgs</c>); a
		/// boxed <c>Int32</c> matches none of them and falls all the way through to <c>TryParseLong</c>, which
		/// re-parses it from <c>obj.ToString()</c>. Widening here keeps that off every downstream operation.
		/// Everything outside the numeric family is passed through untouched.
		/// </summary>
		internal static Expression NormalizeReturn(Expression value, Type type)
		{
			var kind = KindOf(type);

			if (!IsNarrow(kind))
				return Expression.Convert(value, typeof(object));

			var widened = kind switch
			{
				Kind.Single => Expression.Convert(value, typeof(double)),
				// There is no UIntPtr -> Int64 coercion operator, so nuint alone needs the ulong stepping stone.
				Kind.NUInt => Expression.Convert(Expression.Convert(value, typeof(ulong)), typeof(long)),
				_ => Expression.Convert(value, typeof(long)),
			};
			return Expression.Convert(widened, typeof(object));
		}

		/// <summary>
		/// Runtime twin of <see cref="NormalizeReturn"/>, for the reflection-based getters that only ever have a
		/// boxed value in hand. Returns <paramref name="value"/> itself when there is nothing to widen, which is
		/// how <c>ManagedInvoke.ConvertOut</c> tells a script scalar from a CLR object it has to wrap.
		/// </summary>
		internal static object NormalizeScalar(object value) =>
			value switch
			{
				int i => (long)i,
				uint ui => (long)ui,
				short s => (long)s,
				ushort us => (long)us,
				byte b => (long)b,
				sbyte sb => (long)sb,
				ulong ul => unchecked((long)ul),
				nint ni => (long)ni,
				nuint nu => unchecked((long)(ulong)nu),
				float f => (double)f,
				_ => value,
			};

		/// <summary>
		/// True when a member whose declared type is <paramref name="t"/> can hand a script a value no script
		/// type covers — a raw CLR object (List&lt;T&gt;, DateTime, decimal, a boxed enum, …), or an
		/// <c>object</c> holding one — so the inline-C# boundary must route the result through
		/// <c>ManagedInvoke.ConvertOut</c> at run time. The script scalars and Any-derived types cannot leak,
		/// and the narrow numerics are already widened by <see cref="NormalizeReturn"/>/<see cref="NormalizeScalar"/>,
		/// so they all answer false and skip the conversion entirely when the delegate is built.
		/// </summary>
		internal static bool CanLeakClrValue(Type t) =>
			t != typeof(void)
			&& !typeof(Any).IsAssignableFrom(t)
			&& KindOf(t) is Kind.None or Kind.Cast or Kind.Enum;

		/// <summary>
		/// Wraps <paramref name="value"/> in <c>ManagedInvoke.ConvertOut</c>, the same policy <c>Ks.Clr</c>
		/// applies to a value leaving CLR code for a script: scalars pass, narrow numerics widen, a
		/// <c>Type</c> becomes a <c>ManagedType</c>, and any other CLR object a <c>ManagedInstance</c>.
		/// </summary>
		internal static Expression ConvertOut(Expression value) =>
			Expression.Call(convertOutMethod, Expression.Convert(value, typeof(object)));

		// Bound once, and loudly: a signature change would otherwise leave a null MethodInfo that only surfaces as
		// an ArgumentNullException from inside expression building, far from the cause.
		private static MethodInfo Bind(Type t, string name, params Type[] args) =>
			t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, args)
			?? throw new MissingMethodException(t.FullName, name);

		private static readonly MethodInfo tryLongMethod = Bind(typeof(ArgCoercer), nameof(TryLong), typeof(object), typeof(long).MakeByRefType());
		private static readonly MethodInfo tryDoubleMethod = Bind(typeof(ArgCoercer), nameof(TryDouble), typeof(object), typeof(double).MakeByRefType());
		private static readonly MethodInfo tryBoolMethod = Bind(typeof(ArgCoercer), nameof(TryBool), typeof(object), typeof(bool).MakeByRefType());
		private static readonly MethodInfo tryTextMethod = Bind(typeof(ArgCoercer), nameof(TryText), typeof(object), typeof(string).MakeByRefType());
		private static readonly MethodInfo tryCoerceCastMethod = Bind(typeof(ArgCoercer), nameof(TryCoerceCast), typeof(object), typeof(Type), typeof(object).MakeByRefType());
		private static readonly MethodInfo tryCoerceEnumMethod = Bind(typeof(ArgCoercer), nameof(TryCoerceEnum), typeof(object), typeof(Type), typeof(object).MakeByRefType());
		private static readonly MethodInfo tryCoerceBoundaryCastMethod = Bind(typeof(ArgCoercer), nameof(TryCoerceBoundaryCast), typeof(object), typeof(Type), typeof(object).MakeByRefType());
		private static readonly MethodInfo tryCoerceByteSpanMethod = Bind(typeof(ArgCoercer), nameof(TryCoerceByteSpan), typeof(object), typeof(Span<byte>).MakeByRefType());
		private static readonly MethodInfo tryCoerceReadOnlyByteSpanMethod = Bind(typeof(ArgCoercer), nameof(TryCoerceReadOnlyByteSpan), typeof(object), typeof(ReadOnlySpan<byte>).MakeByRefType());
		private static readonly MethodInfo convertOutMethod = Bind(typeof(ManagedInvoke), nameof(ManagedInvoke.ConvertOut), typeof(object));

		/// <summary>What a compiled call a conversion stopped returns: <see cref="Script.DefaultObject"/>, read when it stops.</summary>
		internal static readonly Expression DefaultObjectResult = Expression.Convert(Expression.Property(null,
			typeof(Script).GetProperty(nameof(Script.DefaultObject), BindingFlags.NonPublic | BindingFlags.Static)
			?? throw new MissingMemberException(typeof(Script).FullName, nameof(Script.DefaultObject))), typeof(object));
	}
}
