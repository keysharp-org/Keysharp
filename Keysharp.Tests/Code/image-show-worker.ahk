#ErrorStdOut
#Warn All, StdOut
#Warn Experimental, Off
#NoTrayIcon
#Import Ks { Image, RealThread, Await, A_RealThread }
#Include <assert>

; Native enumeration avoids depending on desktop window-search permissions to close the preview.
#CSharp
public static bool PreviewWindow(bool close)
{
#if WINDOWS
    foreach (System.Windows.Forms.Form window in System.Windows.Forms.Application.OpenForms)
        if (window.Visible && window.Text.StartsWith("Image worker wait"))
#else
    foreach (var window in Eto.Forms.Application.Instance.Windows)
        if (window.Visible && window.Title.StartsWith("Image worker wait"))
#endif
        {
            if (close) window.Close();
            return true;
        }
    return false;
}
#EndCSharp

Thread "Interrupt", 0
for stopWorker in [false, true] {
    global posted := 0, worker := RealThread(ShowPreview)
    workerId := worker.Id
    SetTimer(CheckPreview, 10)
    try {
        completed := worker.Task.Wait(10000)
        Assert(completed, A_LineNumber)
        if completed {
            Assert(posted && posted.IsSucceeded, A_LineNumber)
            if posted && posted.IsSucceeded
                AssertEq(Await(posted), workerId, A_LineNumber)
            AssertEq(worker.Task.IsCanceled, stopWorker, A_LineNumber)
            if !stopWorker
                AssertEq(Await(worker.Task), workerId, A_LineNumber)
            Assert(!PreviewWindow(false), A_LineNumber)
        }
    } finally {
        SetTimer(CheckPreview, 0)
        if worker.IsAlive
            worker.Exit().Wait(2000)
        PreviewWindow(true)
    }
}

ShowPreview() {
    previewImage := Image.Create(20, 12, "Blue")
    try {
        preview := previewImage.Show("Image worker nonblocking", false)
        preview.Destroy()
        previewImage.Show("Image worker wait", true)
        return A_RealThread.Id
    } finally
        previewImage.Dispose()
}

CheckPreview() {
    global posted
    if !PreviewWindow(false)
        return
    if !posted
        posted := worker.Post(() => A_RealThread.Id)
    if posted.IsSucceeded {
        SetTimer(CheckPreview, 0)
        if stopWorker
            worker.Exit()
        else
            PreviewWindow(true)
    }
}

FileAppend "pass", "*"
