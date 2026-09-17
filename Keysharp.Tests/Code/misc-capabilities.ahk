#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import KS { RequestCapabilities }
#Include <assert>

#if WINDOWS
onWindows := true
#else
onWindows := false
#endif

; With no names nothing is requested, so no prompt is shown and every capability just reports its status.
caps := RequestCapabilities()

for capName in ["InputMonitoring", "InputControl", "WindowMonitoring", "WindowControl", "ScreenCapture", "AudioCapture", "CameraCapture", "ClipboardMonitoring"]
{
	if onWindows
		AssertEq(caps.%capName%, "NotApplicable", A_LineNumber)
	else
		Assert(HasProp(caps, capName) && caps.%capName% ~= "^(Granted|Denied|NotApplicable|Unsupported)$", A_LineNumber)
}

if onWindows
	AssertEq(caps.IsGranted, 1, A_LineNumber)
else
	Assert(caps.IsGranted == 0 || caps.IsGranted == 1, A_LineNumber)

Assert(!HasProp(caps, "AccessibilityAutomation") && !HasProp(caps, "InputInjection") && !HasProp(caps, "BlockInput"), A_LineNumber)

; Only the canonical names are accepted, and a rejected name raises before anything is requested.
for rejected in ["AccessibilityAutomation", "InputInjection", "BlockInput", "hook", "inputhook", "synthinput", "sendinput", "capture", "imagecapture", "accessibility", "automation", "input-monitoring", "input_monitoring"]
	Throws(() => RequestCapabilities(rejected), A_LineNumber, ValueError)

FileAppend "pass", "*"
