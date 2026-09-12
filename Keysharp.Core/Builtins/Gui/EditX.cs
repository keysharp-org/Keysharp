namespace Keysharp.Builtins
{
	public static class EditX
	{
		public static long EditGetCurrentCol(object controlID,
											 object winTitle = null,
											 object winText = null,
											 object excludeTitle = null,
											 object excludeText = null) => Platform.Control.EditGetCurrentCol(
													 controlID,
													 winTitle,
													 winText,
													 excludeTitle,
													 excludeText);

		public static long EditGetCurrentLine(object controlID,
											  object winTitle = null,
											  object winText = null,
											  object excludeTitle = null,
											  object excludeText = null) => Platform.Control.EditGetCurrentLine(
													  controlID,
													  winTitle,
													  winText,
													  excludeTitle,
													  excludeText);

		public static string EditGetLine(object n,
										 object controlID,
										 object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null) => Platform.Control.EditGetLine(
											 n.Ai(),
											 controlID,
											 winTitle,
											 winText,
											 excludeTitle,
											 excludeText);

		public static long EditGetLineCount(object controlID,
											object winTitle = null,
											object winText = null,
											object excludeTitle = null,
											object excludeText = null) => Platform.Control.EditGetLineCount(
												controlID,
												winTitle,
												winText,
												excludeTitle,
												excludeText);

		public static string EditGetSelectedText(object controlID,
				object winTitle = null,
				object winText = null,
				object excludeTitle = null,
				object excludeText = null) => Platform.Control.EditGetSelectedText(
					controlID,
					winTitle,
					winText,
					excludeTitle,
					excludeText);

		public static object EditPaste(object @string,
									   object controlID,
									   object winTitle = null,
									   object winText = null,
									   object excludeTitle = null,
									   object excludeText = null)
		{
			Platform.Control.EditPaste(
				@string.As(),
				controlID,
				winTitle,
				winText,
				excludeTitle,
				excludeText);
			return DefaultObject;
		}
	}
}