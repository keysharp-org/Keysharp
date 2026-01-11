#if WINDOWS
using Label = System.Windows.Forms.Label;
using Timer = System.Windows.Forms.Timer;

namespace Keysharp.Core
{
	internal class InputDialog : Form
	{
		private const int CP_NOCLOSE_BUTTON = 0x200;
		private readonly Button btnCancel;
		private readonly Button btnOK;
		private readonly Label prompt;
		private Timer timer;
		private readonly TextBox txtMessage;
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string Default { get; set; }

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string Message
		{
			get => txtMessage.Text;
			set => txtMessage.Text = value;
			//set => txtMessage.Invoke(() => txtMessage.Text = value);
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string PasswordChar
		{
			get => txtMessage.PasswordChar.ToString();

			set
			{
				if (string.IsNullOrEmpty(value))
					txtMessage.UseSystemPasswordChar = true;
				else
					txtMessage.PasswordChar = value[0];
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string Prompt
		{
			get => prompt.Text;
			set => prompt.Text = value;
			//set => prompt.Invoke(() => prompt.Text = value);
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string Result { get; private set; } = "";

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int Timeout { get; set; }

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string Title
		{
			get => Text;
			set => Text = value;
			//set => this.Invoke(() => Text = value);
		}

		protected override CreateParams CreateParams
		{
			get
			{
				var myCp = base.CreateParams;
				myCp.ClassStyle |= CP_NOCLOSE_BUTTON;
				return myCp;
			}
		}

		public InputDialog()
		{
			prompt = new Label();
			btnOK = new Button();
			btnCancel = new Button();
			txtMessage = new TextBox();
			Load += InputDialog_Load;
			Shown += InputDialog_Shown;
			Resize += InputDialog_Resize;
			txtMessage.KeyDown += TxtMessage_KeyDown;
		}

		private void btnCancel_Click(object sender, EventArgs e)
		{
			Result = "Cancel";
			Hide();
		}

		private void btnOK_Click(object sender, EventArgs e)
		{
			Result = "OK";
			Hide();
		}

		private void InitializeComponent()
		{
			SuspendLayout();
			Controls.Add(prompt);//Must be added to sizes are properly computed.
			Controls.Add(txtMessage);
			Controls.Add(btnOK);
			Controls.Add(btnCancel);
			//
			// label1
			//
			prompt.Location = new Point(Margin.Left, Margin.Top);
			prompt.Name = "Prompt";
			prompt.MaximumSize = new Size(150, int.MaxValue);
			prompt.AutoSize = true;
			prompt.Anchor = AnchorStyles.Left | AnchorStyles.Top;
			//
			// txtMessage
			//
			txtMessage.Location = new Point(Margin.Left, prompt.Bottom + Margin.Bottom);
			txtMessage.Name = "Message";
			txtMessage.TabIndex = 0;
			txtMessage.Anchor = AnchorStyles.Left | AnchorStyles.Top;
			//
			// btnOK
			//
			btnOK.DialogResult = DialogResult.OK;
			btnOK.Location = new Point(Margin.Left, txtMessage.Bottom + Margin.Bottom);
			btnOK.Name = "OK";
			btnOK.TabIndex = 1;
			btnOK.Text = "OK";
			btnOK.Click += btnOK_Click;
			btnOK.AutoSize = true;
			btnOK.UseVisualStyleBackColor = true;
			btnOK.Anchor = AnchorStyles.Left | AnchorStyles.Top;
			//
			// btnCancel
			//
			btnCancel.DialogResult = DialogResult.Cancel;
			btnCancel.Location = new Point(btnOK.Right + Margin.Right, btnOK.Top);
			btnCancel.Name = "Cancel";
			btnCancel.TabIndex = 2;
			btnCancel.Text = "Cancel";
			btnCancel.Click += btnCancel_Click;
			btnCancel.AutoSize = true;
			btnCancel.UseVisualStyleBackColor = true;
			btnCancel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
			//
			// InputDialog
			//
			ShowIcon = true;
			MaximizeBox = false;
			MinimizeBox = false;
			//Can't set controlbox to false, else icon will not show.
			FormBorderStyle = FormBorderStyle.FixedDialog;//Disallow resizing.
			SizeGripStyle = SizeGripStyle.Hide;
			Name = "KeysharpInputBox";
			ResumeLayout(false);
			PerformLayout();
		}

		private void InputDialog_Load(object sender, EventArgs e)
		{
			var x = (int)Math.Round(Font.Size * 1.25f);//Not really sure if Size is the same as height, like the documentation says.//TODO
			var y = (int)Math.Round(Font.Size * 0.75f);
			Margin = new Padding(x, y, x, y);
			InitializeComponent();
			txtMessage.Text = Default;
			txtMessage.Width = btnCancel.Right - Margin.Left;
			ClientSize = new Size(btnCancel.Right + Margin.Right, btnOK.Bottom + Margin.Bottom);
		}

		private void InputDialog_Resize(object sender, EventArgs e)
		{
			prompt.MaximumSize = new Size(ClientSize.Width - (Margin.Left + Margin.Right), int.MaxValue);
			txtMessage.Location = new Point(Margin.Left, prompt.Bottom + Margin.Bottom);
			txtMessage.Width = ClientSize.Width - (Margin.Left + Margin.Right);
			btnOK.Location = new Point(Margin.Left, txtMessage.Bottom + Margin.Bottom);
			btnCancel.Location = new Point(btnOK.Right + Margin.Right, btnOK.Top);
		}

		private void InputDialog_Shown(object sender, EventArgs e)
		{
			var script = Script.TheScript;
			
			if (script.Tray?.Icon != null)
				Icon = script.Tray.Icon;

			if (Timeout > 0)
			{
				timer = new Timer
				{
					Interval = Timeout * 1000
				};
				timer.Tick += Timer_Tick;
				timer.Enabled = true;
			}
		}

		private void Timer_Tick(object sender, EventArgs e)
		{
			timer.Enabled = false;
			btnCancel.PerformClick();
		}

		private void TxtMessage_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Enter)
			{
				e.Handled = true;
				e.SuppressKeyPress = true;
				btnOK.PerformClick();
			}
			else if (e.KeyCode == Keys.Escape)
				btnCancel.PerformClick();
		}
	}
}
#endif
