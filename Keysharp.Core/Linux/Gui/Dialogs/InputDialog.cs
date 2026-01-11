#if !WINDOWS
using System.ComponentModel;
using Eto.Forms;
using Eto.Drawing;

namespace Keysharp.Core
{
	internal class InputDialog : Dialog
	{
		private readonly Button btnCancel;
		private readonly Button btnOK;
		private readonly Forms.Label prompt;
		private readonly PasswordBox txtMessage;
		private UITimer timer;

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string Default { get; set; }

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string Message
		{
			get => txtMessage.Text;
			set => txtMessage.Text = value;
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string PasswordChar
		{
			get => txtMessage.PasswordChar.ToString();
			set
			{
				if (string.IsNullOrEmpty(value))
					txtMessage.PasswordChar = '\0';
				else
					txtMessage.PasswordChar = value[0];
			}
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string Prompt
		{
			get => prompt.Text;
			set => prompt.Text = value;
		}

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string Result { get; private set; } = "";

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public int Timeout { get; set; }

		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public new string Title
		{
			get => base.Title;
			set => base.Title = value;
		}

		public InputDialog()
		{
			prompt = new Forms.Label();
			txtMessage = new ();
			btnOK = new Button { Text = "OK" };
			btnCancel = new Button { Text = "Cancel" };

			btnOK.Click += (_, _) =>
			{
				Result = "OK";
				Close();
			};
			btnCancel.Click += (_, _) =>
			{
				Result = "Cancel";
				Close();
			};

			DefaultButton = btnOK;
			AbortButton = btnCancel;
			Resizable = false;

			Content = new StackLayout
			{
				Padding = new Padding(10),
				Spacing = 8,
				Items =
				{
					prompt,
					txtMessage,
					new StackLayout
					{
						Orientation = Orientation.Horizontal,
						Spacing = 8,
						Items = { btnOK, btnCancel }
					}
				}
			};

			Shown += InputDialog_Shown;
		}

		private void InputDialog_Shown(object sender, System.EventArgs e)
		{
			var script = Script.TheScript;
			if (script.Tray?.Icon != null)
				Icon = script.Tray.Icon as Icon;

			txtMessage.Text = Default;
			txtMessage.Focus();

			if (Timeout > 0)
			{
				timer = new UITimer { Interval = Timeout };
				timer.Elapsed += (_, _) =>
				{
					timer.Stop();
					Result = "Cancel";
					Close();
				};
				timer.Start();
			}
		}
	}
}
#endif
