using Keysharp.Builtins;

namespace Keysharp.Runtime.Keyboard
{
	[PublicHiddenFromUser]
	public static class HotstringManager
	{
		public static object AddHotstring(
			string name,
			KeysharpFunc funcObj,
			ReadOnlySpan<char> options,
			string hotstring,
			string replacement,
			bool hasContinuationSection,
			int suspend = 0)
			=> AddDeclaration(name, funcObj, options, hotstring, replacement, hasContinuationSection, suspend, false);

		public static object AddHotstring(
			string name,
			KeysharpFunc funcObj,
			ReadOnlySpan<char> options,
			string hotstring,
			string replacement,
			bool hasContinuationSection,
			bool suspendExempt)
			=> AddDeclaration(name, funcObj, options, hotstring, replacement, hasContinuationSection, 0, suspendExempt);

		private static object AddDeclaration(
			string name,
			KeysharpFunc funcObj,
			ReadOnlySpan<char> options,
			string hotstring,
			string replacement,
			bool hasContinuationSection,
			int suspend,
			bool suspendExempt)
		{
			var manager = Script.TheScript.HotstringManager;
			var result = manager.AddHotstring(
				name,
				funcObj,
				options,
				hotstring,
				replacement,
				hasContinuationSection,
				suspend,
				suspendExempt);
			if (result is Keysharp.Internals.Input.Keyboard.HotstringDefinition { suspended: 0 })
				manager.enabledCount++;
			return result;
		}
	}
}
