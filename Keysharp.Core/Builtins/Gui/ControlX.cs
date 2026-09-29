namespace Keysharp.Builtins
{
	public static class ControlX
	{
		private static void EnsureControlPermission(string operation)
			=> WindowHelper.EnsureWindowControlPermission(operation);

		private static void EnsureControlMonitoringPermission(string operation)
			=> WindowHelper.EnsureWindowMonitoringPermission(operation);

		private static void EnsureControlInputPermission(string operation)
			=> _ = Script.TheScript.Permissions.EnsureInputControl(operation: operation);

		public static long ControlAddItem(object @string,
										  object controlID,
										  object winTitle = null,
										  object winText = null,
										  object excludeTitle = null,
										  object excludeText = null)
		{
			EnsureControlPermission("ControlAddItem");

			if (!@string.CoerceString(out var str))
				return 0L;

			return Platform.Control.ControlAddItem(
											  str,
											  controlID,
											  winTitle,
											  winText,
											  excludeTitle,
											  excludeText);
		}

		public static object ControlChooseIndex(object n,
												object controlID,
												object winTitle = null,
												object winText = null,
												object excludeTitle = null,
												object excludeText = null)
		{
			EnsureControlPermission("ControlChooseIndex");

			if (!n.CoerceInt(out var index))
				return DefaultObject;

			Platform.Control.ControlChooseIndex(
				index,
				controlID,
				winTitle,
				winText,
				excludeTitle,
				excludeText);
			return DefaultObject;
		}

		public static long ControlChooseString(object @string,
											   object controlID,
											   object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null)
		{
			EnsureControlPermission("ControlChooseString");

			if (!@string.CoerceString(out var str))
				return 0L;

			return Platform.Control.ControlChooseString(
													   str,
													   controlID,
													   winTitle,
													   winText,
													   excludeTitle,
												   excludeText);
		}

		public static object ControlClick(object controlOrPos = null,
										  object winTitle = null,
										  object winText = null,
										  object whichButton = null,
										  object clickCount = null,
										  object options = null,
										  object excludeTitle = null,
										  object excludeText = null)
		{
			EnsureControlPermission("ControlClick");
			EnsureControlInputPermission("ControlClick");

			if (!whichButton.CoerceString(out var button) || !options.CoerceString(out var opts))
				return DefaultObject;

			if (!clickCount.CoerceInt(out var count, 1))
				return DefaultObject;

			Platform.Control.ControlClick(
				controlOrPos,
				winTitle,
				winText,
				button,
				count,
				opts,
				excludeTitle,
				excludeText);
			return DefaultObject;
		}

		public static object ControlDeleteItem(object n,
											   object controlID,
											   object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null)
		{
			EnsureControlPermission("ControlDeleteItem");

			if (!n.CoerceInt(out var index))
				return DefaultObject;

			Platform.Control.ControlDeleteItem(
				index,
				controlID,
				winTitle,
				winText,
				excludeTitle,
				excludeText);
			return DefaultObject;
		}

		public static long ControlFindItem(object @string,
										   object controlID,
										   object winTitle = null,
										   object winText = null,
										   object excludeTitle = null,
										   object excludeText = null)
		{
			EnsureControlMonitoringPermission("ControlFindItem");

			if (!@string.CoerceString(out var str))
				return 0L;

			return Platform.Control.ControlFindItem(
											   str,
											   controlID,
											   winTitle,
											   winText,
											   excludeTitle,
											   excludeText);
		}

		public static object ControlFocus(object controlID,
										  object winTitle = null,
										  object winText = null,
										  object excludeTitle = null,
										  object excludeText = null)
		{
			EnsureControlPermission("ControlFocus");
			Platform.Control.ControlFocus(
				controlID,
				winTitle,
				winText,
				excludeTitle,
				excludeText);
			return DefaultObject;
		}

		public static long ControlGetChecked(object controlID,
											 object winTitle = null,
											 object winText = null,
											 object excludeTitle = null,
											 object excludeText = null)
		{
			EnsureControlMonitoringPermission("ControlGetChecked");
			return Platform.Control.ControlGetChecked(
													 controlID,
													 winTitle,
													 winText,
													 excludeTitle,
													 excludeText);
		}

		public static string ControlGetChoice(object controlID,
											  object winTitle = null,
											  object winText = null,
											  object excludeTitle = null,
											  object excludeText = null)
		{
			EnsureControlMonitoringPermission("ControlGetChoice");
			return Platform.Control.ControlGetChoice(
													  controlID,
													  winTitle,
													  winText,
													  excludeTitle,
													  excludeText);
		}

		public static string ControlGetClassNN(object controlID,
											   object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null)
		{
			EnsureControlMonitoringPermission("ControlGetClassNN");
			return Platform.Control.ControlGetClassNN(
													   controlID,
													   winTitle,
													   winText,
													   excludeTitle,
													   excludeText);
		}

		public static long ControlGetEnabled(object controlID,
											 object winTitle = null,
											 object winText = null,
											 object excludeTitle = null,
											 object excludeText = null)
		{
			EnsureControlMonitoringPermission("ControlGetEnabled");
			return Platform.Control.ControlGetEnabled(
													 controlID,
													 winTitle,
													 winText,
													 excludeTitle,
													 excludeText);
		}

		public static long ControlGetExStyle(object controlID,
											 object winTitle = null,
											 object winText = null,
											 object excludeTitle = null,
											 object excludeText = null)
		{
			EnsureControlMonitoringPermission("ControlGetExStyle");
			return Platform.Control.ControlGetExStyle(
													 controlID,
													 winTitle,
													 winText,
													 excludeTitle,
													 excludeText);
		}

		public static long ControlGetFocus(object winTitle = null,
										   object winText = null,
										   object excludeTitle = null,
										   object excludeText = null)
		{
			EnsureControlMonitoringPermission("ControlGetFocus");
			return Platform.Control.ControlGetFocus(
											   winTitle,
											   winText,
											   excludeTitle,
											   excludeText);
		}

		public static long ControlGetHwnd(object controlID,
										  object winTitle = null,
										  object winText = null,
										  object excludeTitle = null,
										  object excludeText = null)
		{
			EnsureControlMonitoringPermission("ControlGetHwnd");
			return Platform.Control.ControlGetHwnd(
											  controlID,
											  winTitle,
											  winText,
											  excludeTitle,
											  excludeText);
		}

		public static long ControlGetIndex(object controlID,
										   object winTitle = null,
										   object winText = null,
										   object excludeTitle = null,
										   object excludeText = null)
		{
			EnsureControlMonitoringPermission("ControlGetIndex");
			return Platform.Control.ControlGetIndex(
											   controlID,
											   winTitle,
											   winText,
											   excludeTitle,
											   excludeText);
		}

		public static object ControlGetItems(object controlID,
											object winTitle = null,
											object winText = null,
											object excludeTitle = null,
											object excludeText = null)
		{
			EnsureControlMonitoringPermission("ControlGetItems");
			return Platform.Control.ControlGetItems(
												controlID,
												winTitle,
												winText,
												excludeTitle,
												excludeText);
		}

		public static object ControlGetPos([ByRef] object outX = null,
										   [ByRef] object outY = null,
										   [ByRef] object outWidth = null,
										   [ByRef] object outHeight = null,
										   object controlID = null,
										   object winTitle = null,
										   object winText = null,
										   object excludeTitle = null,
										   object excludeText = null)
		{
			EnsureControlMonitoringPermission("ControlGetPos");
			object valX = null, valY = null, valWidth = null, valHeight = null;
			Platform.Control.ControlGetPos(
				ref valX,
				ref valY,
				ref valWidth,
				ref valHeight,
				controlID,
				winTitle,
				winText,
				excludeTitle,
				excludeText);
			if (outX != null) Refs.SetValue(outX, valX);
			if (outY != null) Refs.SetValue(outY, valY);
			if (outWidth != null) Refs.SetValue(outWidth, valWidth);
			if (outHeight != null) Refs.SetValue(outHeight, valHeight);
			return DefaultObject;
		}

		public static long ControlGetStyle(object controlID,
										   object winTitle = null,
										   object winText = null,
										   object excludeTitle = null,
										   object excludeText = null)
		{
			EnsureControlMonitoringPermission("ControlGetStyle");
			return Platform.Control.ControlGetStyle(
											   controlID,
											   winTitle,
											   winText,
											   excludeTitle,
											   excludeText);
		}

		public static string ControlGetText(object controlID,
											object winTitle = null,
											object winText = null,
											object excludeTitle = null,
											object excludeText = null)
		{
			EnsureControlMonitoringPermission("ControlGetText");
			return Platform.Control.ControlGetText(
												controlID,
												winTitle,
												winText,
												excludeTitle,
												excludeText);
		}

		public static long ControlGetVisible(object controlID,
											 object winTitle = null,
											 object winText = null,
											 object excludeTitle = null,
											 object excludeText = null)
		{
			EnsureControlMonitoringPermission("ControlGetVisible");
			return Platform.Control.ControlGetVisible(
													 controlID,
													 winTitle,
													 winText,
													 excludeTitle,
													 excludeText);
		}

		public static object ControlHide(object controlID,
										 object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null)
		{
			EnsureControlPermission("ControlHide");
			Platform.Control.ControlHide(
				controlID,
				winTitle,
				winText,
				excludeTitle,
				excludeText);
			return DefaultObject;
		}

		public static object ControlHideDropDown(object controlID,
				object winTitle = null,
				object winText = null,
				object excludeTitle = null,
				object excludeText = null)
		{
			EnsureControlPermission("ControlHideDropDown");
			Platform.Control.ControlHideDropDown(
				controlID,
				winTitle,
				winText,
				excludeTitle,
				excludeText);
			return DefaultObject;
		}

		public static object ControlMove(object x = null,
										 object y = null,
										 object width = null,
										 object height = null,
										 object controlID = null,
										 object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null)
		{
			EnsureControlPermission("ControlMove");
			int xVal = int.MinValue, yVal = int.MinValue, widthVal = int.MinValue, heightVal = int.MinValue;

			if ((x is not null && !x.CoerceInt(out xVal))
					|| (y is not null && !y.CoerceInt(out yVal))
					|| (width is not null && !width.CoerceInt(out widthVal))
					|| (height is not null && !height.CoerceInt(out heightVal)))
				return DefaultObject;

			Platform.Control.ControlMove(
				xVal,
				yVal,
				widthVal,
				heightVal,
				controlID,
				winTitle,
				winText,
				excludeTitle,
				excludeText);
			return DefaultObject;
		}

		public static object ControlSend(object keys,
										 object controlID = null,
										 object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null)
		{
			EnsureControlPermission("ControlSend");
			EnsureControlInputPermission("ControlSend");

			if (!keys.CoerceString(out var keysText))
				return DefaultObject;

			Platform.Control.ControlSend(
				keysText,
				controlID,
				winTitle,
				winText,
				excludeTitle,
				excludeText);
			return DefaultObject;
		}

		public static object ControlSendText(object keys,
											 object controlID = null,
											 object winTitle = null,
											 object winText = null,
											 object excludeTitle = null,
											 object excludeText = null)
		{
			EnsureControlPermission("ControlSendText");
			EnsureControlInputPermission("ControlSendText");

			if (!keys.CoerceString(out var keysText))
				return DefaultObject;

			Platform.Control.ControlSendText(
				keysText,
				controlID,
				winTitle,
				winText,
				excludeTitle,
				excludeText);
			return DefaultObject;
		}

		public static object ControlSetChecked(object newSetting,
											   object controlID,
											   object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null)
		{
			EnsureControlPermission("ControlSetChecked");
			Platform.Control.ControlSetChecked(
				newSetting,
				controlID,
				winTitle,
				winText,
				excludeTitle,
				excludeText);
			return DefaultObject;
		}

		public static object ControlSetEnabled(object newSetting,
											   object controlID,
											   object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null)
		{
			EnsureControlPermission("ControlSetEnabled");
			Platform.Control.ControlSetEnabled(
				newSetting,
				controlID,
				winTitle,
				winText,
				excludeTitle,
				excludeText);
			return DefaultObject;
		}

		public static object ControlSetExStyle(object value,
											   object controlID,
											   object winTitle = null,
											   object winText = null,
											   object excludeTitle = null,
											   object excludeText = null)
		{
			EnsureControlPermission("ControlSetExStyle");
			Platform.Control.ControlSetExStyle(
				value,
				controlID,
				winTitle,
				winText,
				excludeTitle,
				excludeText);
			return DefaultObject;
		}

		public static object ControlSetStyle(object value,
											 object controlID,
											 object winTitle = null,
											 object winText = null,
											 object excludeTitle = null,
											 object excludeText = null)
		{
			EnsureControlPermission("ControlSetStyle");
			Platform.Control.ControlSetStyle(
				value,
				controlID,
				winTitle,
				winText,
				excludeTitle,
				excludeText);
			return DefaultObject;
		}

		public static object ControlSetText(object newText,
											object controlID,
											object winTitle = null,
											object winText = null,
											object excludeTitle = null,
											object excludeText = null)
		{
			EnsureControlPermission("ControlSetText");

			if (!newText.CoerceString(out var text))
				return DefaultObject;

			Platform.Control.ControlSetText(
				text,
				controlID,
				winTitle,
				winText,
				excludeTitle,
				excludeText);
			return DefaultObject;
		}

		public static object ControlShow(object controlID,
										 object winTitle = null,
										 object winText = null,
										 object excludeTitle = null,
										 object excludeText = null)
		{
			EnsureControlPermission("ControlShow");
			Platform.Control.ControlShow(
				controlID,
				winTitle,
				winText,
				excludeTitle,
				excludeText);
			return DefaultObject;
		}

		public static object ControlShowDropDown(object controlID,
				object winTitle = null,
				object winText = null,
				object excludeTitle = null,
				object excludeText = null)
		{
			EnsureControlPermission("ControlShowDropDown");
			Platform.Control.ControlShowDropDown(
				controlID,
				winTitle,
				winText,
				excludeTitle,
				excludeText);
			return DefaultObject;
		}
	}
}
