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
			Assert.IsTrue(typeof(Keysharp.Core.KeysharpException).IsAssignableTo(typeof(System.Exception)));
			Assert.IsTrue(typeof(Keysharp.Core.Error).IsAssignableTo(typeof(Keysharp.Core.KeysharpException)));
			Assert.IsTrue(typeof(Keysharp.Core.ParseException).IsAssignableTo(typeof(Keysharp.Core.Error)));
			Assert.IsTrue(typeof(Keysharp.Core.IndexError).IsAssignableTo(typeof(Keysharp.Core.Error)));
			Assert.IsTrue(typeof(Keysharp.Core.KeyError).IsAssignableTo(typeof(Keysharp.Core.Error)));
			Assert.IsTrue(typeof(Keysharp.Core.MemberError).IsAssignableTo(typeof(Keysharp.Core.UnsetError)));
			Assert.IsTrue(typeof(Keysharp.Core.UnsetItemError).IsAssignableTo(typeof(Keysharp.Core.UnsetError)));
			Assert.IsTrue(typeof(Keysharp.Core.MemoryError).IsAssignableTo(typeof(Keysharp.Core.Error)));
			Assert.IsTrue(typeof(Keysharp.Core.MethodError).IsAssignableTo(typeof(Keysharp.Core.MemberError)));
			Assert.IsTrue(typeof(Keysharp.Core.PropertyError).IsAssignableTo(typeof(Keysharp.Core.MemberError)));
			Assert.IsTrue(typeof(Keysharp.Core.OSError).IsAssignableTo(typeof(Keysharp.Core.Error)));
			Assert.IsTrue(typeof(Keysharp.Core.TargetError).IsAssignableTo(typeof(Keysharp.Core.Error)));
			Assert.IsTrue(typeof(Keysharp.Core.TimeoutError).IsAssignableTo(typeof(Keysharp.Core.Error)));
			Assert.IsTrue(typeof(Keysharp.Core.TypeError).IsAssignableTo(typeof(Keysharp.Core.Error)));
			Assert.IsTrue(typeof(Keysharp.Core.ValueError).IsAssignableTo(typeof(Keysharp.Core.Error)));
			Assert.IsTrue(typeof(Keysharp.Core.ZeroDivisionError).IsAssignableTo(typeof(Keysharp.Core.Error)));
#if LINUX
			Assert.IsTrue(typeof(Keysharp.Core.ClipboardAll).IsAssignableTo(typeof(Keysharp.Core.Common.ObjectBase.KeysharpObject)));
#elif WINDOWS
			Assert.IsTrue(typeof(Keysharp.Core.ClipboardAll).IsAssignableTo(typeof(Keysharp.Core.Buffer)));
#endif
			Assert.IsTrue(typeof(Keysharp.Core.Buffer).IsAssignableTo(typeof(Keysharp.Core.Common.ObjectBase.KeysharpObject)));
			Assert.IsTrue(typeof(Keysharp.Core.Array).IsAssignableTo(typeof(Keysharp.Core.Common.ObjectBase.KeysharpObject)));
			Assert.IsTrue(typeof(Keysharp.Core.Map).IsAssignableTo(typeof(Keysharp.Core.Common.ObjectBase.KeysharpObject)));
			Assert.IsTrue(typeof(Keysharp.Core.Common.File.KeysharpFile).IsAssignableTo(typeof(Keysharp.Core.Common.ObjectBase.KeysharpObject)));
			Assert.IsTrue(Keysharp.Core.Types.Type(0L) == "Integer");
			Assert.IsTrue(Keysharp.Core.Types.Type(1.2) == "Float");
			Assert.IsTrue(Keysharp.Core.Types.Type(new KeysharpObject()) == "Object");
			Assert.IsTrue(Keysharp.Core.Types.Type(null) == "unset");
			//Assure every public static function returns something other than void.
			var loadedAssemblies = GetLoadedAssemblies();
			var types = loadedAssemblies.Values.Where(asm => asm.FullName.StartsWith("Keysharp.Core,"))
						.SelectMany(t => GetNestedTypes(t.GetExportedTypes()))
						.Where(t => t.GetCustomAttribute<PublicHiddenFromUser>() == null && t.Namespace != null && t.Namespace.StartsWith("Keysharp.Core")
							   && t.Namespace != "Keysharp.Core.Properties"
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
	}
}