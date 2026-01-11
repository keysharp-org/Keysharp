namespace Keysharp.Core.Common.Window
{
	partial class AboutBox : Form
	{
		public AboutBox()
		{
			InitializeComponent();
			Text = $"About Keysharp";
			labelProductName.Text = $"Keysharp {AssemblyVersion}";
			textBoxDescription.Text = @"A C# port and improvement of AutoHotkey.

Authors:
	Matt Feemster 2020 - present
	Descolada 2024 - present
	IronAHK developers 2010 - 1015

Testers:
	Burque505

Acknowledgements:
	See website above.
";
		}

		public string AssemblyVersion => Assembly.GetExecutingAssembly().GetName().Version.ToString();

		private void okButton_Click(object sender, EventArgs e) => Close();

		private void linkLabel_LinkClicked(object sender, System.Windows.Forms.LinkLabelLinkClickedEventArgs e) => _ = Process.Start(new ProcessStartInfo(linkLabel.Text) { UseShellExecute = true });
	}
}
