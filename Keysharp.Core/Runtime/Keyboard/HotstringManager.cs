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
			=> Script.TheScript.HotstringManager.AddHotstring(
				name,
				funcObj,
				options,
				hotstring,
				replacement,
				hasContinuationSection,
				suspend);
	}
}
