using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Keysharp.Internals.Invoke;

namespace Keysharp.Tests
{
	public partial class TypeTests : TestRunner
	{
		/// <summary>
		/// Ensure the type hierarchy matches the documentation exactly.
		/// </summary>
		[Test, Category("Types")]
		public void TestTypes()
		{
			Assert.IsTrue(typeof(Keysharp.Builtins.KeysharpException).IsAssignableTo(typeof(System.Exception)));
			Assert.IsTrue(typeof(Keysharp.Builtins.ParseException).IsAssignableTo(typeof(System.Exception)));
			// The script-visible class hierarchy and Type() results are checked in types-conversions.ahk.
			Assert.AreEqual("unset", Keysharp.Builtins.Types.Type(null));
			var types = typeof(Any).Assembly.GetExportedTypes()
						.Where(t => t.Namespace?.StartsWith("Keysharp.Builtins", StringComparison.Ordinal) == true
							   && t.Namespace != "Keysharp.Builtins.Properties" && t.IsClass
							   && ((t.IsAbstract && t.IsSealed) || t.IsAssignableTo(typeof(Any))) && IsUserVisible(t))
						.ToArray();
			var methods = types.SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
						.Where(m => m.GetCustomAttribute<PublicHiddenFromUser>() == null).ToArray();

			//Assure every public static function returns something other than void.
			foreach (var method in methods.Where(m => m.IsStatic && !m.IsSpecialName))
			{
				Assert.IsTrue(method.ReturnType != typeof(void), $"Method {method.DeclaringType?.FullName}.{method.Name} should not return void.");
			}

			foreach (var method in methods.Where(m => !m.IsSpecialName))
				AssertParameterNames(method, method.GetParameters());

			foreach (var indexer in types.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
					 .Where(p => p.GetIndexParameters().Length != 0))
				AssertParameterNames(indexer, indexer.GetIndexParameters());

			static bool IsUserVisible(Type type)
			{
				for (var current = type; current != null; current = current.DeclaringType)
					if (current.GetCustomAttribute<PublicHiddenFromUser>() != null)
						return false;

				return true;
			}

			static void AssertParameterNames(MemberInfo member, IEnumerable<ParameterInfo> parameters)
			{
				var holder = member switch
				{
					MethodInfo method => MethodPropertyHolder.GetOrAdd(method),
					PropertyInfo property => MethodPropertyHolder.GetOrAdd(property),
					_ => null
				};
				var names = holder?.ParamScan.ToDictionary(parameter => parameter.Index, parameter => parameter.Name)
					?? new Dictionary<int, string>();

				foreach (var parameter in parameters)
				{
					if (!names.TryGetValue(parameter.Position, out var name))
						continue;

					var declared = parameter.GetCustomAttribute<UserDeclaredNameAttribute>()?.Name;
					var exempt = parameter.GetCustomAttribute<ParamArrayAttribute>() != null || declared is "wParam" or "lParam";
					Assert.IsTrue(exempt || name.Length > 0 && char.IsUpper(name[0]),
						$"Parameter {member.DeclaringType?.FullName}.{member.Name}({name}) should use PascalCase.");
				}
			}
		}

		/// <summary>
		/// Verifies the numeric conversion semantics: TypeError on invalid input where a number
		/// is required (AHK v2 parity), Float truncation toward zero in integer contexts, and
		/// canonical Float-to-string formatting (whole-valued Floats keep a trailing .0).
		/// </summary>
		[Test, Category("Types"), NonParallelizable]
		public void TypesConversions() => Assert.IsTrue(TestScript("types-conversions", true));

		/// <summary>
		/// Verifies that a value reaching a <em>typed</em> CLR parameter, property or field through the
		/// dynamic-invoke path is converted with AutoHotkey's rules rather than unboxed. Every case here used
		/// to raise an InvalidCastException/ArgumentException out of the compiled core, which is not a
		/// KeysharpException and therefore killed the process past any script try/catch.
		/// Also pins the two things the conversion must NOT do: touch the packed variadic slot, or leave a
		/// non-canonical numeric type (a boxed Int32) visible to the script.
		/// </summary>
		[Test, Category("Types"), NonParallelizable]
		public void TypesTypedParams() => Assert.IsTrue(TestScript("types-typed-params", true));

		/// <summary>
		/// Ks.Boolean, the type of a truth value. The language already produced booleans -- every comparison,
		/// every negation, Map.Has -- with nothing able to name them, and nothing able to tell one from the
		/// Integer 1 it reads as. This pins both halves: it behaves as an Integer everywhere, and it is a
		/// distinct type when asked.
		/// </summary>
		[Test, Category("Types"), NonParallelizable]
		public void TypesBoolean() => Assert.IsTrue(TestScript("types-boolean", true));

	}
}
