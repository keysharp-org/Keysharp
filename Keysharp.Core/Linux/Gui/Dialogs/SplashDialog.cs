#if !WINDOWS
namespace Keysharp.Core
{
	public class SplashDialog : Form, IComplexDialog
	{
		private readonly Forms.Label main;
		private readonly Forms.Label sub;
		private readonly ImageView pic;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public Image Image
		{
			get => pic.Image;
			set => pic.Image = value;
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string MainText
		{
			get => main.Text;
			set
			{
				main.Text = value;
				main.Visible = !string.IsNullOrEmpty(main.Text);
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string SubText
		{
			get => sub.Text;
			set
			{
				sub.Text = value;
				sub.Visible = !string.IsNullOrEmpty(sub.Text);
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public new string Title
		{
			get => base.Title;
			set => base.Title = value;
		}

		public SplashDialog()
		{
			var baseFont = SystemFonts.Default(12);
			main = new Forms.Label
			{
				TextAlignment = TextAlignment.Center,
				Font = new Font(baseFont.FamilyName, baseFont.Size, FontStyle.Bold)
			};
			sub = new Forms.Label { TextAlignment = TextAlignment.Center };
			pic = new ImageView();

			Content = new StackLayout
			{
				Padding = new Padding(10),
				Spacing = 8,
				Items =
				{
					main,
					pic,
					sub
				}
			};

			Resizable = false;
			ShowInTaskbar = false;
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
