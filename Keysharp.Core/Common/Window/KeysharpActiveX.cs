#if WINDOWS
namespace Keysharp.Core.Common.Window;

internal class KeysharpActiveX : UserControl
{
	private static bool loadedDll = false;//Ok to keep this static because as long as it's loaded once per process, it's ok.

	/// <summary>
	/// Required designer variable.
	/// </summary>
	private readonly System.ComponentModel.IContainer components = null;

	/// <summary>
	/// Required reference so that accessing the underlying __ComObject does not throw an exception
	/// about the runtime callable wrapper being disconnected from its underlying
	/// COM object.
	/// </summary>
	private object ob;

	[DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
	public string AxText { get; set; }

	[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
	internal ComObject Iid { get; private set; }

	protected override CreateParams CreateParams
	{
		get
		{
			var cp = base.CreateParams;
			cp.Style |= WindowsAPI.WS_CLIPSIBLINGS;
			return cp;
		}
	}

	public KeysharpActiveX(string text)
	{
		AxText = text;
		InitializeComponent();
		//this.Load += KeysharpActiveX_Load;

		if (!loadedDll)
		{
			var result = AtlAxWinInit();

			if (result == 0)
			{
				_ = Errors.ErrorOccurred($"Initializing ActiveX with AtlAxWinInit() failed.");
			}
			else
				loadedDll = true;
		}
	}

	[DllImport("atl.dll", CharSet = CharSet.Unicode)]
	public static extern int AtlAxCreateControl(
		string lpszName,
		nint hWnd,
		nint pStream,
		[MarshalAs(UnmanagedType.IDispatch)] out object ppUnkContainer);

	[DllImport("atl.dll", CharSet = CharSet.Unicode)]
	public static extern int AtlAxWinInit();

	internal void Init()
	{
		if (loadedDll)
		{
			_ = Marshal.GetHINSTANCE(GetType().Module);

			if (AtlAxCreateControl(AxText, Handle, 0, out ob) >= 0)
			//if (axHandle != 0)
			{
				Console.WriteLine("AtlAxCreateControl() succeeded.");

				Iid = ob is IDispatch iid ? new ComObject(VarEnum.VT_VARIANT, iid) : new ComObject(VarEnum.VT_UNKNOWN, ob);
			}
		}
	}

	/// <summary>
	/// Clean up any resources being used.
	/// </summary>
	/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
	protected override void Dispose(bool disposing)
	{
		if (disposing && (components != null))
		{
			components.Dispose();
		}

		base.Dispose(disposing);
	}

	#region Component Designer generated code

	/// <summary>
	/// Required method for Designer support - do not modify
	/// the contents of this method with the code editor.
	/// </summary>
	private void InitializeComponent()
	{
		SuspendLayout();
		AutoScaleDimensions = new SizeF(8F, 20F);
		AutoScaleMode = AutoScaleMode.Dpi;
		Name = "KeysharpActiveX";
		Size = new Size(500, 500);
		ResumeLayout(false);
	}

	#endregion Component Designer generated code
}

#endif