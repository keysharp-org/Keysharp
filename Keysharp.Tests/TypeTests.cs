using Assert = NUnit.Framework.Legacy.ClassicAssert;

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
			Assert.IsTrue(typeof(Keysharp.Builtins.Error).IsAssignableTo(typeof(Keysharp.Builtins.Any)));
			Assert.IsTrue(typeof(Keysharp.Builtins.IndexError).IsAssignableTo(typeof(Keysharp.Builtins.Error)));
			Assert.IsTrue(typeof(Keysharp.Builtins.KeyError).IsAssignableTo(typeof(Keysharp.Builtins.Error)));
			Assert.IsTrue(typeof(Keysharp.Builtins.MemberError).IsAssignableTo(typeof(Keysharp.Builtins.UnsetError)));
			Assert.IsTrue(typeof(Keysharp.Builtins.UnsetItemError).IsAssignableTo(typeof(Keysharp.Builtins.UnsetError)));
			Assert.IsTrue(typeof(Keysharp.Builtins.MemoryError).IsAssignableTo(typeof(Keysharp.Builtins.Error)));
			Assert.IsTrue(typeof(Keysharp.Builtins.MethodError).IsAssignableTo(typeof(Keysharp.Builtins.MemberError)));
			Assert.IsTrue(typeof(Keysharp.Builtins.PropertyError).IsAssignableTo(typeof(Keysharp.Builtins.MemberError)));
			Assert.IsTrue(typeof(Keysharp.Builtins.OSError).IsAssignableTo(typeof(Keysharp.Builtins.Error)));
			Assert.IsTrue(typeof(Keysharp.Builtins.TargetError).IsAssignableTo(typeof(Keysharp.Builtins.Error)));
			Assert.IsTrue(typeof(Keysharp.Builtins.TimeoutError).IsAssignableTo(typeof(Keysharp.Builtins.Error)));
			Assert.IsTrue(typeof(Keysharp.Builtins.TypeError).IsAssignableTo(typeof(Keysharp.Builtins.Error)));
			Assert.IsTrue(typeof(Keysharp.Builtins.ValueError).IsAssignableTo(typeof(Keysharp.Builtins.Error)));
			Assert.IsTrue(typeof(Keysharp.Builtins.ZeroDivisionError).IsAssignableTo(typeof(Keysharp.Builtins.Error)));
#if LINUX
			Assert.IsTrue(typeof(Keysharp.Builtins.ClipboardAll).IsAssignableTo(typeof(Keysharp.Builtins.KeysharpObject)));
#elif WINDOWS
			Assert.IsTrue(typeof(Keysharp.Builtins.ClipboardAll).IsAssignableTo(typeof(Keysharp.Builtins.Buffer)));
#endif
			Assert.IsTrue(typeof(Keysharp.Builtins.Buffer).IsAssignableTo(typeof(Keysharp.Builtins.KeysharpObject)));
			Assert.IsTrue(typeof(Keysharp.Builtins.Array).IsAssignableTo(typeof(Keysharp.Builtins.KeysharpObject)));
			Assert.IsTrue(typeof(Keysharp.Builtins.Map).IsAssignableTo(typeof(Keysharp.Builtins.KeysharpObject)));
			Assert.IsTrue(typeof(Keysharp.Builtins.KeysharpFile).IsAssignableTo(typeof(Keysharp.Builtins.KeysharpObject)));
			Assert.IsTrue(Keysharp.Builtins.Types.Type(0L) == "Integer");
			Assert.IsTrue(Keysharp.Builtins.Types.Type(1.2) == "Float");
			Assert.IsTrue(Keysharp.Builtins.Types.Type(new KeysharpObject()) == "Object");
			Assert.IsTrue(Keysharp.Builtins.Types.Type(null) == "unset");
			//Assure every public static function returns something other than void.
			var loadedAssemblies = GetLoadedAssemblies();
			var types = loadedAssemblies.Values.Where(asm => asm.FullName.StartsWith("Keysharp.Builtins,"))
						.SelectMany(t => GetNestedTypes(t.GetExportedTypes()))
						.Where(t => t.GetCustomAttribute<PublicHiddenFromUser>() == null && t.Namespace != null && t.Namespace.StartsWith("Keysharp.Builtins")
							   && t.Namespace != "Keysharp.Builtins.Properties"
							   && t.IsClass && (t.IsPublic || t.IsNestedPublic));

			foreach (var method in types
					 .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
					 .Where(m => !m.IsSpecialName && m.GetCustomAttribute<PublicHiddenFromUser>() == null))
			{
				Assert.IsTrue(method.ReturnType != typeof(void), $"Method {method.DeclaringType?.FullName}.{method.Name} should not return void.");
			}
		}

		private static Dictionary<string, Assembly> GetLoadedAssemblies()
		{
			var assemblies = AppDomain.CurrentDomain.GetAssemblies();
			var dkt = new Dictionary<string, Assembly>(assemblies.Length);

			foreach (var assembly in assemblies)
			{
				try
				{
					if (!assembly.IsDynamic)
						dkt[assembly.Location] = assembly;
				}
				catch (Exception)
				{
				}
			}

			return dkt;
		}

		private static IEnumerable<Type> GetNestedTypes(Type[] types)
		{
			foreach (var t in types)
			{
				yield return t;

				foreach (var nested in GetNestedTypes(t.GetNestedTypes()))
					yield return nested;
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
	}
}
