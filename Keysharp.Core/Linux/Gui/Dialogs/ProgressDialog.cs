#if !WINDOWS
using System;
using System.ComponentModel;
using Eto.Forms;
using Eto.Drawing;

namespace Keysharp.Core
{
	public class ProgressDialog : Form, IComplexDialog
	{
		private readonly Forms.Label mainText;
		private readonly ProgressBar progressBar;
		private readonly Forms.Label subText;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string MainText
		{
			get => mainText.Text;
			set => mainText.Text = value;
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int ProgressMaximum
		{
			get => (int)progressBar.MaxValue;
			set => progressBar.MaxValue = value;
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int ProgressMinimum
		{
			get => (int)progressBar.MinValue;
			set => progressBar.MinValue = value;
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int ProgressValue
		{
			get => (int)progressBar.Value;
			set
			{
				try
				{
					progressBar.Value = value;
				}
				catch (ArgumentException)
				{
					// for now, we ignore wrong number ranges
				}
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string SubText
		{
			get => subText.Text;
			set => subText.Text = value;
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public new string Title
		{
			get => base.Title;
			set => base.Title = value;
		}

		public ProgressDialog()
		{
			var baseFont = SystemFonts.Default(12);
			mainText = new Forms.Label
			{
				TextAlignment = TextAlignment.Center,
				Font = new Font(baseFont.FamilyName, baseFont.Size, FontStyle.Bold)
			};
			progressBar = new ProgressBar
			{
				MinValue = 0,
				MaxValue = 100,
				Value = 0
			};
			subText = new Forms.Label { TextAlignment = TextAlignment.Center };

			Content = new StackLayout
			{
				Padding = new Padding(10),
				Spacing = 8,
				Items =
				{
					mainText,
					progressBar,
					subText
				}
			};

			Resizable = false;
			ShowInTaskbar = true;
			WindowStyle = WindowStyle.Default;
			Shown += (_, _) => CenterOnPrimaryScreen();
		}

		public bool InvokeRequired => Application.Instance != null && !Application.Instance.IsUIThread;

		public bool TopMost
		{
			get => Topmost;
			set => Topmost = value;
		}

		public object Invoke(Delegate method, params object[] obj) =>
			Application.Instance.Invoke(() => method.DynamicInvoke(obj));

		public object Invoke(Delegate method) =>
			Application.Instance.Invoke(() => method.DynamicInvoke());

		private void CenterOnPrimaryScreen()
		{
			var area = Forms.Screen.PrimaryScreen?.WorkingArea;
			if (area == null)
				return;

			var x = area.Value.X + (area.Value.Width - Width) / 2;
			var y = area.Value.Y + (area.Value.Height - Height) / 2;
			Location = new Point((int)x, (int)y);
		}
	}
}
#endif
