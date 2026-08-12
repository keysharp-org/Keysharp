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
	/// <item>reference targets — a checked cast that raises a <see cref="TypeError"/> naming both types instead
	/// of an uncatchable <see cref="InvalidCastException"/>. Unset passes through as null.</item>
	/// </list>
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
		internal enum Kind { None, Long, Double, Bool, Str, Cast, Int, UInt, Short, UShort, Byte, SByte, ULong, NInt, NUInt, Single }

		/// <summary>The conversion rule for <paramref name="t"/>, or <see cref="Kind.None"/> to leave it alone.</summary>
		internal static Kind KindOf(Type t)
		{
			// An enum reports its underlying integral type, and byref/pointer types report as non-value types, so
			// all of them have to be rejected up front or the switch below would claim them for a kind that cannot
			// represent them.
			if (t.IsEnum || t.IsByRef || t.IsPointer || t.IsFunctionPointer)
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
		internal static object CoerceCast(object value, Type target)
		{
			if (value == null || target.IsInstanceOfType(value))
				return value;

			// TypeErrorOccurred returns its fallback instead of throwing when an OnError handler suppresses the
			// error, and that fallback is DefaultObject ("" or null), which would then fail the cast this feeds.
			// Null is the only value assignable to every reference target, so normalize to it.
			var fallback = Errors.TypeErrorOccurred(value, target);
			return target.IsInstanceOfType(fallback) ? fallback : null;
		}

		/// <summary>Unwraps managed proxies at an explicit CLR boundary.</summary>
		internal static object CoerceBoundaryCast(object value, Type target)
		{
			// ManagedInstance represents its payload even for an object slot. ManagedType remains a script object for
			// object, matching Ks.Clr, and unwraps only for Type-compatible targets.
			if (value is Ks.Clr.ManagedInstance mi && (target == typeof(object) || target.IsInstanceOfType(mi._instance)))
				return mi._instance;

			if (value == null || target.IsInstanceOfType(value))
				return value;

			if (value is Ks.Clr.ManagedType mt && target.IsInstanceOfType(mt._type))
				return mt._type;

			var fallback = Errors.TypeErrorOccurred(value, target);
			return target.IsInstanceOfType(fallback)
				? fallback
				: target.IsValueType && Nullable.GetUnderlyingType(target) == null
					? Activator.CreateInstance(target)
					: null;
		}

		/// <summary>
		/// Wraps <paramref name="value"/> (an <c>object</c> expression) so it yields <paramref name="target"/>.
		/// </summary>
		internal static Expression Coerce(Expression value, Type target)
		{
			switch (KindOf(target))
			{
				case Kind.None: return Expression.Convert(value, target);

				case Kind.Long: return AsLong();

				case Kind.Double: return AsDouble();

				case Kind.Bool: return Expression.Call(forceBoolMethod, value);

				case Kind.Str: return Expression.Call(asStringMethod, value, emptyString);

				case Kind.Cast:
					return Expression.Convert(Expression.Call(coerceCastMethod, value, Expression.Constant(target, typeof(Type))), target);

				case Kind.Single: return Expression.Convert(AsDouble(), target);

				// There is no Int64 -> UIntPtr coercion operator, so nuint alone needs the ulong stepping stone.
				case Kind.NUInt: return Expression.Convert(Expression.Convert(AsLong(), typeof(ulong)), target);

				default: return Expression.Convert(AsLong(), target);//The narrow integral kinds. Expression.Convert is unchecked.
			}

			Expression AsLong() => Expression.Call(toLongMethod, value, allowFloat);
			Expression AsDouble() => Expression.Call(toDoubleMethod, value);
		}

		/// <summary>Builds the input conversion for a member explicitly marked as a CLR boundary.</summary>
		internal static Expression CoerceBoundary(Expression value, Type target)
		{
			var kind = KindOf(target);

			if (NeedsBoundaryCast(target, kind))
				return Expression.Convert(Expression.Call(coerceBoundaryCastMethod, value, Expression.Constant(target, typeof(Type))), target);

			return Coerce(value, target);
		}

		/// <summary>
		/// Runtime counterpart of <see cref="Coerce"/>, for the paths that never build an expression tree: the Clr
		/// boundary, the reserved-variable (A_*) setters, and every reflection-based property setter.
		/// Returns a value boxed as exactly <paramref name="target"/>.
		/// </summary>
		internal static object CoerceValue(object value, Type target)
		{
			switch (KindOf(target))
			{
				case Kind.Long: return value.ToLong();
				case Kind.Double: return value.ToDouble();
				case Kind.Bool: return ForceBool(value);
				case Kind.Str: return value.As();
				case Kind.Cast: return CoerceCast(value, target);
				case Kind.Int: return unchecked((int)value.ToLong());
				case Kind.UInt: return unchecked((uint)value.ToLong());
				case Kind.Short: return unchecked((short)value.ToLong());
				case Kind.UShort: return unchecked((ushort)value.ToLong());
				case Kind.Byte: return unchecked((byte)value.ToLong());
				case Kind.SByte: return unchecked((sbyte)value.ToLong());
				case Kind.ULong: return unchecked((ulong)value.ToLong());
				case Kind.NInt: return unchecked((nint)value.ToLong());
				case Kind.NUInt: return unchecked((nuint)value.ToLong());
				case Kind.Single: return (float)value.ToDouble();
				default: return value;
			}
		}

		/// <summary>Runtime counterpart of <see cref="CoerceBoundary"/>.</summary>
		internal static object CoerceBoundaryValue(object value, Type target)
		{
			var kind = KindOf(target);

			return NeedsBoundaryCast(target, kind)
				? CoerceBoundaryCast(value, target)
				: CoerceValue(value, target);
		}

		private static bool NeedsBoundaryCast(Type target, Kind kind) =>
			kind == Kind.Cast
			// object[] is Kind.None because a packed params slot must stay untouched. CompileCore skips that slot,
			// leaving a non-variadic object[] free to use the normal CLR-boundary conversion here.
			|| kind == Kind.None && (target == typeof(object) || target == typeof(object[]) || target.IsValueType);

		/// <summary>Whether an ordinary property assignment needs script scalar conversion.</summary>
		internal static bool NeedsCoercion(Type target) => KindOf(target) != Kind.None;

		/// <summary>Whether a CLR-boundary property assignment needs scalar conversion or proxy unwrapping.</summary>
		internal static bool NeedsBoundaryCoercion(Type target)
		{
			var kind = KindOf(target);
			return kind != Kind.None || NeedsBoundaryCast(target, kind);
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
			&& KindOf(t) is Kind.None or Kind.Cast;

		/// <summary>
		/// Wraps <paramref name="value"/> in <c>ManagedInvoke.ConvertOut</c>, the same policy <c>Ks.Clr</c>
		/// applies to a value leaving CLR code for a script: scalars pass, narrow numerics widen, a
		/// <c>Type</c> becomes a <c>ManagedType</c>, and any other CLR object a <c>ManagedInstance</c>.
		/// </summary>
		internal static Expression ConvertOut(Expression value) =>
			Expression.Call(convertOutMethod, Expression.Convert(value, typeof(object)));

		private static readonly Expression allowFloat = Expression.Constant(true);
		private static readonly Expression emptyString = Expression.Constant("", typeof(string));

		// Bound once, and loudly: a signature change would otherwise leave a null MethodInfo that only surfaces as
		// an ArgumentNullException from inside expression building, far from the cause.
		private static MethodInfo Bind(Type t, string name, params Type[] args) =>
			t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, args)
			?? throw new MissingMethodException(t.FullName, name);

		private static readonly MethodInfo toLongMethod = Bind(typeof(ObjectExtensions), nameof(ObjectExtensions.ToLong), typeof(object), typeof(bool));
		private static readonly MethodInfo toDoubleMethod = Bind(typeof(ObjectExtensions), nameof(ObjectExtensions.ToDouble), typeof(object));
		private static readonly MethodInfo forceBoolMethod = Bind(typeof(Script), nameof(Script.ForceBool), typeof(object));
		// `.As()`, not Script.ForceString: it honors a script class's own ToString() override, which is what
		// AutoHotkey does where a string is expected, and it is what the Clr boundary already used.
		private static readonly MethodInfo asStringMethod = Bind(typeof(ObjectExtensions), nameof(ObjectExtensions.As), typeof(object), typeof(string));
		private static readonly MethodInfo coerceCastMethod = Bind(typeof(ArgCoercer), nameof(CoerceCast), typeof(object), typeof(Type));
		private static readonly MethodInfo coerceBoundaryCastMethod = Bind(typeof(ArgCoercer), nameof(CoerceBoundaryCast), typeof(object), typeof(Type));
		private static readonly MethodInfo convertOutMethod = Bind(typeof(ManagedInvoke), nameof(ManagedInvoke.ConvertOut), typeof(object));
	}
}
