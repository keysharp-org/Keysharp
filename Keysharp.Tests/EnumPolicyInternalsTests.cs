namespace Keysharp.Tests;

public class EnumPolicyInternalsTests
{
	// Native dialog results cannot be exercised through a script without opening a modal window.
	[Test, Category("Internal"), Category("Curated")]
	public void MessageBoxResultNames()
	{
#if WINDOWS
		Assert.That(Dialogs.MessageBoxResultName(System.Windows.Forms.DialogResult.OK), Is.EqualTo("OK"));
		Assert.That(Dialogs.MessageBoxResultName(System.Windows.Forms.DialogResult.Cancel), Is.EqualTo("Cancel"));
		Assert.That(Dialogs.MessageBoxResultName(System.Windows.Forms.DialogResult.TryAgain), Is.EqualTo("TryAgain"));
#else
		Assert.AreEqual("OK", Dialogs.MessageBoxResultName(Eto.Forms.DialogResult.Ok));
		Assert.AreEqual("Cancel", Dialogs.MessageBoxResultName(Eto.Forms.DialogResult.Cancel));
#endif
	}
}
