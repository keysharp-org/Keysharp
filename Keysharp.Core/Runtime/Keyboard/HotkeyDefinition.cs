using Keysharp.Builtins;

namespace Keysharp.Runtime.Keyboard
{
	[PublicHiddenFromUser]
	public static class HotkeyDefinition
	{
		public static object AddHotkey(KeysharpFunc callback, uint hookAction, string name)
			=> Keysharp.Internals.Input.Keyboard.HotkeyDefinition.AddHotkey(Script.TheScript, callback, hookAction, name);

		public static object ManifestAllHotkeysHotstringsHooks()
			=> Keysharp.Internals.Input.Keyboard.HotkeyDefinition.ManifestAllHotkeysHotstringsHooks(Script.TheScript);
	}
}
