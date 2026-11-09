#ErrorStdOut
#Warn All, StdOut
#Warn Experimental, Off
#NoTrayIcon
#Requires AutoHotkey v2.0
#Import Ks { A_Thread, App, Audio, Font, Image }
#Include <assert>

#CSharp
public static object MetadataProcess() => new Keysharp.Builtins.ScriptProcess(System.Diagnostics.Process.GetCurrentProcess());
#EndCSharp

for name in ["Copyright", "Trademark", "ExitReason"]
    CheckOptional(App, name)
Assert(App.Name != "", A_LineNumber)
CheckValue21(App, "ExitCode", 0)
CheckOptional(A_Thread, "ExitCode")

optionalFont := Font()
for name in ["Name", "Size", "Color", "Weight", "Quality", "Bold", "Italic", "Underline", "Strike"]
    CheckOptional(optionalFont, name)
CheckValue21(optionalFont, "Options", "")
optionalFont.Bold := false
optionalFont.Italic := false
optionalFont.Quality := 0
CheckValue21(optionalFont, "Bold", false)
CheckValue21(optionalFont, "Italic", false)
CheckValue21(optionalFont, "Quality", 0)
optionalFont.Bold := ""
CheckOptional(optionalFont, "Bold")

optionalImage := Image.Create(2, 2)
try {
    CheckValue21(optionalImage, "OriginX", 0)
    CheckValue21(optionalImage, "ScaleX", 1)
    optionalImage.Rotate(90)
    for name in ["OriginX", "OriginY", "ScaleX", "ScaleY"]
        CheckOptional(optionalImage, name)
    optionalImage.SetOrigin(0, 0, 1, 1)
    CheckValue21(optionalImage, "OriginX", 0)
    CheckValue21(optionalImage, "ScaleX", 1)
} finally
    optionalImage.Dispose()

output := Audio.Output()
try {
    for name in ["Device", "SampleRate", "Channels", "LatencyMilliseconds", "Peak", "Error"]
        CheckOptional(output, name)
    CheckValue21(output, "ActiveVoiceCount", 0)
} finally
    output.Dispose()

recorder := Audio.Recorder()
try {
    for name in ["Device", "Path", "Peak", "Error", "Result"]
        CheckOptional(recorder, name)
    CheckValue21(recorder, "DurationMilliseconds", 0)
} finally
    recorder.Dispose()

process := MetadataProcess()
try {
    CheckOptional(process, "ExitCode")
    CheckOptional(process, "ExitTime")
    CheckValue21(process, "IsExited", false)
} finally
    process.Close()

FileAppend "pass", "*"

CheckOptional(value, name) {
    #Requires AutoHotkey v2.0
    Assert(HasProp(value, name), A_LineNumber)
    AssertEq(value.%name%, "", A_LineNumber)
    CheckMissing21(value, name)
    AssertEq(value.%name%, "", A_LineNumber)
}

CheckMissing21(value, name) {
    #Requires AutoHotkey v2.1-alpha
    Assert(HasProp(value, name), A_LineNumber)
    Assert(!IsSet(value.%name%?), A_LineNumber)
    AssertEq(value.%name% ?? "missing", "missing", A_LineNumber)
    Throws(() => value.%name%, A_LineNumber, UnsetError)
}

CheckValue21(value, name, expected) {
    #Requires AutoHotkey v2.1-alpha
    AssertEq(value.%name%, expected, A_LineNumber)
    AssertEq(value.%name% ?? "missing", expected, A_LineNumber)
}
