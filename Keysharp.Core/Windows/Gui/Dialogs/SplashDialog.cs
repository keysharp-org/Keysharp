#if WINDOWS
using Label = System.Windows.Forms.Label;

namespace Keysharp.Core
{
	public class SplashDialog : Form, IComplexDialog
	{
		private TableLayoutPanel layoutTable;
		private Label main, sub;
		private PictureBox pic;

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
		public string Title
		{
			get => Text;
			set => Text = value;
		}

		public SplashDialog() => InitializeComponent();

		private void InitializeComponent()
		{
			layoutTable = new TableLayoutPanel();
			main = new Label();
			sub = new Label();
			pic = new PictureBox();
			SuspendLayout();
			//
			// Layout Table
			//
			layoutTable.ColumnCount = 1;
			layoutTable.RowCount = 3;
			layoutTable.GrowStyle = TableLayoutPanelGrowStyle.AddColumns;
			_ = layoutTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			layoutTable.AutoSize = true;
			layoutTable.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			//
			// Main Text
			//
			main.Dock = DockStyle.Fill;
			main.Font = new Font(main.Font, FontStyle.Bold);
			main.TextAlign = ContentAlignment.MiddleCenter;
			main.Size = new Size(100, 20);
			main.AutoSize = true;
			main.Location = new Point(0, 30);
			//
			// Pictrue Control
			//
			pic.Dock = DockStyle.Fill;
			pic.SizeMode = PictureBoxSizeMode.AutoSize;
			pic.Size = new Size(100, 20);
			pic.AutoSize = true;
			pic.Location = new Point(0, 40);
			//
			// Sub Text
			//
			sub.Dock = DockStyle.Fill;
			sub.TextAlign = ContentAlignment.MiddleCenter;
			sub.Size = new Size(100, 20);
			sub.AutoSize = true;
			sub.Location = new Point(0, 150);
			layoutTable.Controls.Add(main, 0, 0);
			layoutTable.Controls.Add(pic, 0, 1);
			layoutTable.Controls.Add(sub, 0, 2);
			Controls.Add(layoutTable);
			Size = new Size(500, 400);
			StartPosition = FormStartPosition.CenterScreen;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			ControlBox = true;
			ShowInTaskbar = false;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;
			AutoScaleMode = AutoScaleMode.Dpi;
			AutoSize = true;
			ResumeLayout(true);
		}
	}
}
#endif
