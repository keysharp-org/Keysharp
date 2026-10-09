namespace Keyview;

internal sealed class KeyviewDocumentState
{
	private string savedText = "";

	internal string CurrentFilePath { get; private set; }
	internal bool IsScratch => string.IsNullOrEmpty(CurrentFilePath);
	internal bool CanCompile => !IsScratch && IsSourceFile(CurrentFilePath);
	internal string DisplayName => IsScratch ? "Scratch document" : Path.GetFileName(CurrentFilePath);

	internal void LoadFile(string path, string text)
	{
		CurrentFilePath = Path.GetFullPath(path);
		savedText = text ?? "";
	}

	internal void LoadScratch()
	{
		CurrentFilePath = null;
		savedText = "";
	}

	internal void MarkSaved(string text) => savedText = text ?? "";

	internal bool IsDirty(string text) => !IsScratch && ((text ?? "").Length != savedText.Length || !string.Equals(savedText, text ?? "", StringComparison.Ordinal));

	internal string GetWindowTitle(string baseTitle, bool dirty) =>
		IsScratch
		? $"{baseTitle} — Scratchpad (autosaved)"
		: $"{DisplayName}{(dirty ? " *" : "")} — {baseTitle}";

	internal string GetStatusText(bool dirty) =>
		IsScratch
		? "Scratchpad document — autosaved"
		: $"{CurrentFilePath}{(dirty ? " — Modified" : "")}";

	private static bool IsSourceFile(string path)
	{
		var extension = Path.GetExtension(path);
		return extension.Equals(".ahk", StringComparison.OrdinalIgnoreCase)
			   || extension.Equals(".ks", StringComparison.OrdinalIgnoreCase);
	}
}
