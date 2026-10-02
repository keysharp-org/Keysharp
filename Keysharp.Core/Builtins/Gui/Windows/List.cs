#if WINDOWS
namespace Keysharp.Builtins
{
	public partial class Gui
	{
		/// <summary>The WinForms half of <see cref="Gui.List"/>'s tab handling.</summary>
		public partial class List
		{
			private partial void AddTabs(KeysharpTabControl tc, string[] texts) =>
				tc.TabPages.AddRange(texts.Select(x => new TabPage(x ?? "")).ToArray());

			private partial void RemoveTab(KeysharpTabControl tc, int index)
			{
				//AutoHotkey ties a control to a tab number, so the controls of the deleted tab show on the tab that takes
				//its number.
				var ctrls = tc.TabPages[index].Controls;
				tc.TabPages.RemoveAt(index);

				if (index < tc.TabPages.Count)
				{
					tc.TabPages[index].Controls.Clear();
					tc.TabPages[index].Controls.AddRange(ctrls.Cast<System.Windows.Forms.Control>().ToArray());
				}
			}
		}

		public partial class Tab
		{
			private partial void SetTabImage(KeysharpTabControl tc, int tab, int image) => tc.TabPages[tab].ImageIndex = image;
		}
	}
}
#endif
