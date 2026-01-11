#if !WINDOWS
namespace Keysharp.Core.Common.Window
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

		private void InitializeComponent()
		{
			logoPictureBox = new ImageView
			{
				Size = new Size(128, 128),
				Visible = false
			};

			labelProductName = new Forms.Label
			{
				VerticalAlignment = VerticalAlignment.Center,
				Text = "Keysharp"
			};

			linkLabel = new LinkButton
			{
				Text = "https://github.com/mfeemster/keysharp/tree/master"
			};
			linkLabel.Click += (_, __) => linkLabel_LinkClicked(linkLabel, new System.Windows.Forms.LinkLabelLinkClickedEventArgs());

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

			var descriptionScroll = new Scrollable
			{
				Content = textBoxDescription,
				ExpandContentWidth = true,
				ExpandContentHeight = true
			};

			tableLayoutPanel = new TableLayout
			{
				Padding = new Padding(10),
				Spacing = new Size(8, 6),
				Rows =
				{
					new TableRow(labelProductName),
					new TableRow(linkLabel),
					new TableRow(descriptionScroll) { ScaleHeight = true },
					new TableRow(new StackLayout
					{
						Orientation = Orientation.Horizontal,
						HorizontalContentAlignment = HorizontalAlignment.Right,
						Items = { okButton }
					})
				}
			};

			Content = tableLayoutPanel;
			Size = new Size(854, 303);
			Maximizable = false;
			Minimizable = false;
			ShowInTaskbar = false;
			WindowStyle = WindowStyle.Utility;
			Shown += (_, __) => CenterOnPrimaryScreen();
		}

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
				Location = new Point(x.Ai(), y.Ai());
			} catch {}
		}
	}
}
#endif
