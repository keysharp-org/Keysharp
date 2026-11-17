namespace Keysharp.Builtins
{
	public partial class Ks
	{
		/// <summary>A pseudo-thread's scoped window and coordinate modes.</summary>
		public class CoordWindow : KeysharpObject, IDisposable
		{
			private readonly CoordModeType[] modes = new CoordModeType[5];
			private nint hwnd;
			private bool initialized;
			private int released;

			internal nint Handle => hwnd;
			internal bool IsReleased => Volatile.Read(ref released) != 0;

			public CoordWindow(params object[] Args) : this()
			{
				if (Args != null)
					_ = __New(Args.ElementAtOrDefault(0), Args.ElementAtOrDefault(1), Args.ElementAtOrDefault(2), Args.ElementAtOrDefault(3));
			}

			protected CoordWindow() { }

			public object __New(object WinTitle = null, object WinText = null, object ExcludeTitle = null, object ExcludeText = null)
			{
				if (initialized)
					return Errors.ErrorOccurred("This CoordWindow has already been initialized. Construct another for a second scope.");

				initialized = true;

				if (!WindowSearch.TrySearchWindow(WinTitle, WinText, ExcludeTitle, ExcludeText, out var window, true) || window == null)
					return DefaultObject;

				if (!window.IsSpecified)
					return Errors.TargetErrorOccurred(WinTitle, WinText, ExcludeTitle, ExcludeText);

				hwnd = window.Handle;
				var tv = Script.TheScript.Threads.CurrentThread;
				tv.coordWindows?.RemoveAll(static reference => !reference.TryGetTarget(out var scope) || scope.IsReleased);
				(tv.coordWindows ??= []).Add(new(this));
				return DefaultObject;
			}

			public static object staticget_Current(object This)
				=> Script.TheScript.Threads.CurrentThread.CurrentCoordWindow ?? (object)DefaultObject;

			public object Hwnd => hwnd != 0 ? (long)hwnd : DefaultObject;

			public override object __Delete()
			{
				Release();
				return DefaultObject;
			}

			void IDisposable.Dispose() => Release();

			private void Release()
			{
				Interlocked.Exchange(ref released, 1);
				HasFinalizer = false;
			}

			internal ref CoordModeType CoordModeFor(CoordMode category) => ref modes[(int)category];
		}
	}
}
