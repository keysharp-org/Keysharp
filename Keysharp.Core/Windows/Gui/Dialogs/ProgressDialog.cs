#if WINDOWS
using Label = System.Windows.Forms.Label;

namespace Keysharp.Core
{
	public class ProgressDialog : Form, IComplexDialog
	{
		private TableLayoutPanel layoutTable;
		private Label mainText;
		private ProgressBar progressBar;
		private Label subText;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string MainText
		{
			get => mainText.Text;
			set => mainText.Text = value;
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int ProgressMaximum
		{
			get => progressBar.Maximum;
			set => progressBar.Maximum = value;
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int ProgressMinimum
		{
			get => progressBar.Minimum;
			set => progressBar.Minimum = value;
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int ProgressValue
		{
			get => progressBar.Value;

			set
			{
				try
				{
					progressBar.Value = value;
				}
				catch (ArgumentException)
				{
					// for now, we igonre wrong number ranges
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
		public string Title
		{
			get => Text;
			set => Text = value;
		}

		public ProgressDialog()
		{
			InitializeComponent();
			StartPosition = FormStartPosition.CenterScreen;
		}

		private void InitializeComponent()
		{
			layoutTable = new TableLayoutPanel();
			subText = new Label();
			mainText = new Label();
			progressBar = new ProgressBar();
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
			// mMainText
			//
			mainText.Name = "label1";
			mainText.TextAlign = ContentAlignment.MiddleCenter;
			mainText.Size = new Size(240, 48);
			mainText.TabIndex = 1;
			mainText.Font = new Font(mainText.Font.FontFamily, 12, FontStyle.Bold);
			mainText.Dock = DockStyle.Fill;
			mainText.AutoSize = true;
			//mainText.AutoSizeMode = AutoSizeMode.GrowAndShrink;
			//
			// Progress
			//
			progressBar.Name = "Progress";
			progressBar.Size = new Size(300, 20);
			progressBar.Value = 0;
			progressBar.Dock = DockStyle.Fill;
			progressBar.AutoSize = true;
			//
			// mSubText
			//
			subText.Name = "txtMessage";
			subText.Size = new Size(232, 20);
			subText.TabIndex = 0;
			subText.Dock = DockStyle.Fill;
			subText.AutoSize = true;
			layoutTable.Controls.Add(mainText, 0, 0);
			layoutTable.Controls.Add(progressBar, 0, 1);
			layoutTable.Controls.Add(subText, 0, 2);
			Controls.Add(layoutTable);
			//
			// DialogForm
			//
			AutoScaleBaseSize = new Size(5, 13);
			ClientSize = new Size(320, 151);
			ControlBox = true;
			FormBorderStyle = FormBorderStyle.FixedDialog;
			MaximizeBox = false;
			MinimizeBox = false;
			Name = "ProgressDialog";
			Text = "Progress";
			ResumeLayout(true);
			AutoSize = true;
			AutoSizeMode = AutoSizeMode.GrowAndShrink;
		}
	}
}
#endif
