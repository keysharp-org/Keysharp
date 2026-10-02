#if !WINDOWS
namespace Keysharp.Builtins
{
	public partial class Gui
	{
		/// <summary>
		/// The Eto half of <see cref="Gui.List"/>'s tab handling. Each page raises the control's Click itself, since
		/// the tab control's own mouse events would swallow those of the controls on it.
		/// </summary>
		public partial class List
		{
			private partial void AddTabs(KeysharpTabControl tc, string[] texts)
			{
				foreach (var text in texts)
				{
					var page = new TabPage(KeysharpTabControl.DisplayText(text));
					page.Click += _control_Click;
					tc.TabPages.Add(page);
				}
			}

			private partial void RemoveTab(KeysharpTabControl tc, int index) => tc.TabPages.RemoveAt(index);
		}

		public partial class Tab
		{
			private partial void SetTabImage(KeysharpTabControl tc, int tab, int image) =>
				tc.TabPages[tab].Image = image >= 0 ? tc.ImageList.Images[image] : null;
		}
	}
}
#endif
