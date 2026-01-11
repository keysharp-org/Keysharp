#if WINDOWS
namespace Keysharp.Core.Common.Window
{
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
		internal ComValue Iid { get; private set; }

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
				int result = AtlAxWinInit();

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
			out nint ppUnkContainer);

		[DllImport("atl.dll", CharSet = CharSet.Unicode)]
		public static extern int AtlAxWinInit();

		[DllImport("atl.dll")]
		private static extern int AtlAxGetControl(nint hWnd, out nint ppUnkControl);

		internal void Init()
		{
			if (!loadedDll) return;

			// Create the host (container) – ignore its IUnknown; we’ll fetch the control next.
			_ = AtlAxCreateControl(AxText, Handle, IntPtr.Zero, out var pUnkContainer);
			if (pUnkContainer != 0) Marshal.Release(pUnkContainer); // not needed

			// Get the hosted control’s IUnknown
			if (AtlAxGetControl(Handle, out var pCtrl) >= 0 && pCtrl != 0)
			{
				// Prefer IDispatch for late-binding:
				if (Marshal.QueryInterface(pCtrl, in Com.IID_IDispatch, out var pDisp) == 0)
				{
					// Wrap the control, not the container:
					Iid = new Keysharp.Core.ComObject(VarEnum.VT_DISPATCH, (long)pDisp);
				}
				else
				{
					Iid = new Keysharp.Core.ComValue(VarEnum.VT_UNKNOWN, (long)pCtrl);
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
}

#endif