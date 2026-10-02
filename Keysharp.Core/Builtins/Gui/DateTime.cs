namespace Keysharp.Builtins
{
	public partial class Gui
	{
		/// <summary>The holder for a DateTime control.</summary>
		public partial class DateTime
		{
			/// <summary>Sets the display format: ShortDate, LongDate, Time, or a custom format string.</summary>
			public object SetFormat(object format = null)
			{
				(Ctrl as DateTimePicker)?.SetFormat(format);
				return DefaultObject;
			}
		}
	}
}
