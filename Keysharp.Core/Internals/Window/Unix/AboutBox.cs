using Keysharp.Builtins;
#if !WINDOWS
namespace Keysharp.Internals.Window.Unix
{
	partial class AboutBox : Form
	{
		public string Text
		{
			get => Title;
			set => Title = value;
		}

		private TableLayout tableLayoutPanel;
		private ImageView logoPictureBox;
		private Forms.Label labelProductName;
		private TextArea textBoxDescription;
		private Button okButton;
		private LinkButton linkLabel;

		public AboutBox()
		{
			InitializeComponent();
			Text = AboutInfo.Title;
			labelProductName.Text = AboutInfo.ProductName;
			textBoxDescription.Text = AboutInfo.Description.ReplaceLineEndings(Environment.NewLine);
		}

		private void InitializeComponent()
		{
			//256x256 is the logo's native size and what the WinForms dialog shows; it fits inside the
			//client height below, so it does not push the window past the size set further down.
			logoPictureBox = new ImageView
			{
				Size = new Size(256, 256)
			};

			try
			{
				logoPictureBox.Image = new Bitmap(AboutInfo.LogoBytes);
			}
			catch//A missing/undecodable logo must not take the whole dialog down; the text is the point.
			{
				logoPictureBox.Visible = false;
			}

			labelProductName = new Forms.Label
			{
				VerticalAlignment = VerticalAlignment.Center,
				Text = "Keysharp"
			};

			linkLabel = new LinkButton
			{
				Text = AboutInfo.Url
			};
			linkLabel.Click += linkLabel_LinkClicked;

			textBoxDescription = new TextArea
			{
				ReadOnly = true,
				Wrap = false,
				Text = "Description"
			};

			okButton = new Button
			{
				Text = "&OK"
			};
			okButton.Click += okButton_Click;

			//The logo sits in a left column spanning every row, matching the WinForms layout.
			tableLayoutPanel = new TableLayout
			{
				Padding = new Padding(10),
				Spacing = new Size(8, 6),
				Rows =
				{
					new TableRow(
						new TableCell(new TableLayout
						{
							Rows =
							{
								new TableRow(new TableCell(logoPictureBox, false)),
								new TableRow(new TableCell()) { ScaleHeight = true }
							}
						}, false),
						new TableCell(new TableLayout
						{
							Spacing = new Size(0, 6),
							Rows =
							{
								new TableRow(labelProductName),
								new TableRow(linkLabel),
								new TableRow(textBoxDescription) { ScaleHeight = true },
								new TableRow(new StackLayout
								{
									Orientation = Orientation.Horizontal,
									HorizontalContentAlignment = HorizontalAlignment.Right,
									Items = { okButton }
								})
							}
						}, true)
					) { ScaleHeight = true }
				}
			};

			Content = tableLayoutPanel;
			Size = new Size(854, 303);
			Maximizable = false;
			Minimizable = false;
			ShowInTaskbar = false;
#if !OSX
			WindowStyle = WindowStyle.Utility;
#endif
			Shown += (_, __) => CenterOnPrimaryScreen();
			//Form, unlike Dialog, has no DefaultButton/AbortButton, so wire the usual dismiss keys up by hand.
			KeyDown += (_, e) =>
			{
				if (e.Key == Eto.Forms.Keys.Escape || e.Key == Eto.Forms.Keys.Enter)
				{
					e.Handled = true;
					Close();
				}
			};
		}

		private void okButton_Click(object sender, EventArgs e) => Close();

		private void linkLabel_LinkClicked(object sender, EventArgs e)
			=> _ = Process.Start(new ProcessStartInfo(linkLabel.Text) { UseShellExecute = true });

		private void CenterOnPrimaryScreen()
		{
			var screen = Forms.Screen.PrimaryScreen;
			if (screen == null)
				return;

			try
			{
				var bounds = screen.Bounds;
				var x = bounds.X + (bounds.Width - Size.Width) / 2;
				var y = bounds.Y + (bounds.Height - Size.Height) / 2;
				_ = x.TryCoerceInt(out var px);
				_ = y.TryCoerceInt(out var py);
				Location = new Point(px, py);
			} catch {}
		}
	}
}
#endif
