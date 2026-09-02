using Keysharp.Runtime;

namespace Keysharp.Builtins
{
	/// <summary>
	/// The one place that answers "is this value a reference, and how do I read or write through it".
	/// <para>
	/// There are two questions, and which one applies depends on whether anything DECLARED the parameter an output
	/// variable. Where something did -- a built-in's <c>[ByRef]</c> parameter, a script function's <c>&amp;p</c> --
	/// <see cref="IsRef"/> accepts leniently and lets access report the truth, mirroring AutoHotkey's
	/// <c>ObjectCanBeOutputVar</c>. Where nothing did and the code is inferring from the argument alone -- a DllCall
	/// or COM argument that may equally be a reference or an ordinary value, <c>&amp;x</c> choosing between
	/// forwarding a value and wrapping the variable -- <see cref="DeclaresValue"/> demands proof, because guessing wrong
	/// there is silent.
	/// </para>
	/// <para>
	/// Shape and state are separate: a reference whose target is unset is still a reference -- the call is what fills
	/// it in -- so no caller may read a null <c>__Value</c> as "not a reference".
	/// </para>
	/// </summary>
	[PublicHiddenFromUser]
	public static class Refs
	{
		internal const string ValueName = "__Value";

		/// <summary>
		/// True when <paramref name="value"/> may be used as a reference or output variable. A script object must
		/// actually declare <c>__Value</c>, which is what stops an ordinary Map or Array from silently absorbing an
		/// output; anything else that is not a script object -- a ComValue, a COM object -- cannot answer
		/// <c>HasProp</c> yet may still carry one, so it is accepted and left to fail at access time.
		/// </summary>
		public static bool IsRef(object value) => value switch
		{
			VarRef => true,
			KeysharpObject => DeclaresValue(value),
			Any => true,
			null => false,
#if WINDOWS
			_ => Marshal.IsComObject(value)
#else
			_ => false
#endif
		};

		/// <summary>
		/// True when <paramref name="value"/> provably declares a <c>__Value</c> property, as opposed to merely
		/// being allowed to try one. Says nothing about whether that property currently holds a value -- an unset
		/// reference declares it just the same. See the type summary for which of the two predicates a caller wants.
		/// </summary>
		public static bool DeclaresValue(object value) => value switch
		{
			VarRef => true,
			// A built-in declaring __Value in C#, such as ComValueRef, is registered on its prototype like any other
			// member, so one test answers for built-ins and script classes alike.
			Any any => Functions.HasProp(any, ValueName) != 0,
			_ => false
		};

		/// <summary>Reads through a reference, raising <see cref="UnsetError"/> when its target holds no value.</summary>
		public static object GetValue(object target, [CallerArgumentExpression(nameof(target))] string name = null) =>
			GetValueOrNull(target, name) ?? Errors.UnsetErrorOccurred($"{Param(name)}refers to a variable that has not been assigned a value");

		/// <summary>Reads through a reference, yielding null when its target holds no value.</summary>
		public static object GetValueOrNull(object target, [CallerArgumentExpression(nameof(target))] string name = null)
		{
			if (target is VarRef vr && vr.IsPlain)
				return vr.__Value;

			Demand(target, name: name);
			return Script.GetPropertyValueOrNull(target, ValueName);
		}

		/// <summary>
		/// Writes through a reference. A null <paramref name="target"/> is an omitted output variable and is
		/// discarded, which is what lets a caller skip any parameter it does not want filled in.
		/// </summary>
		public static object SetValue(object target, object value, [CallerArgumentExpression(nameof(target))] string name = null)
		{
			if (target == null)
				return value;

			if (target is VarRef vr && vr.IsPlain)
				return vr.__Value = value;

			Demand(target, name: name);
			return Script.SetRefValue(target, value);
		}

		/// <summary>
		/// Raises a <see cref="TypeError"/> unless <paramref name="target"/> can be used as a reference. Pass
		/// <paramref name="mustBeVarRef"/> where the parameter has to BE one rather than stand in for one -- asking
		/// whether a variable has been assigned is only meaningful for a real reference, so that is what
		/// <c>IsSetRef</c> demands, and an object implementing <c>__Value</c> does not qualify.
		/// </summary>
		public static void Demand(object target, bool mustBeVarRef = false, [CallerArgumentExpression(nameof(target))] string name = null)
		{
			if (mustBeVarRef ? target is VarRef : IsRef(target))
				return;

			_ = Errors.TypeErrorOccurred($"{Param(name)}requires a variable reference, but received "
										 + (target == null ? "no value." : $"a {Types.Type(target)}."));
		}

		// A caller-supplied name arrives as a C# expression -- CallerArgumentExpression fills it in -- so it is only
		// worth printing when it reads as a parameter name rather than, say, an indexing expression. A parameter
		// whose script name is a C# keyword is written `@ref`, and the script knows it without the escape.
		private static string Param(string name)
		{
			var text = name != null && name.StartsWith('@') ? name[1..] : name;
			return !string.IsNullOrEmpty(text) && text[0].IsLeadingIdentifierChar() && text.All(c => c.IsIdentifierChar())
				   ? $"Parameter '{text}' " : "This parameter ";
		}
	}
}
