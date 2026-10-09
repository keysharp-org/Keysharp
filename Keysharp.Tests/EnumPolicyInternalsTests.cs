namespace Keysharp.Tests;

public class EnumPolicyInternalsTests
{
	// Native dialog results cannot be exercised through a script without opening a modal window.
	[Test, Category("Internal"), Category("Curated")]
	public void MessageBoxResultNames()
	{
#if WINDOWS
		Assert.AreEqual("OK", Dialogs.MessageBoxResultName(System.Windows.Forms.DialogResult.OK));
		Assert.AreEqual("Cancel", Dialogs.MessageBoxResultName(System.Windows.Forms.DialogResult.Cancel));
		Assert.AreEqual("TryAgain", Dialogs.MessageBoxResultName(System.Windows.Forms.DialogResult.TryAgain));
#else
		Assert.AreEqual("OK", Dialogs.MessageBoxResultName(Eto.Forms.DialogResult.Ok));
		Assert.AreEqual("Cancel", Dialogs.MessageBoxResultName(Eto.Forms.DialogResult.Cancel));
#endif
	}
}
