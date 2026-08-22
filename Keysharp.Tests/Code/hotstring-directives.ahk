#NoTrayIcon

#import KS { * }
#Hotstring NoMouse
#Include <assert>

Assert(A_DefaultHotstringNoMouse, A_LineNumber)


; Reset to what it was for the sake of other tests in this class.
Hotstring("MouseReset", true)

#Hotstring EndChars -()[]{}':;"/\,.?!`n`s`t

AssertEq(A_DefaultHotstringEndChars, "-()[]{}':;`"/\,.?!`n`s`t", A_LineNumber)
	

; End char required.
newVal := false
origVal := A_DefaultHotstringEndCharRequired

AssertEq(origVal, newVal, A_LineNumber)
	
		
#hotstring * ; Comes after the test above, but actually gets executed before
Hotstring("*0")

Assert(origVal != A_DefaultHotstringEndCharRequired, A_LineNumber)

Assert(A_DefaultHotstringEndCharRequired, A_LineNumber)
	

; Case sensitivity. Will be false by default even though the directive sets it to true,
; because it will have been internally toggled because of the call to C1 below.
newVal := false
origVal := A_DefaultHotstringCaseSensitive

AssertEq(origVal, newVal, A_LineNumber)

#Hotstring C ; Runs before test above.

AssertEq(origVal, A_DefaultHotstringCaseSensitive, A_LineNumber)

AssertEq(newVal, A_DefaultHotstringCaseSensitive, A_LineNumber)

; Case sensitivity restore to default.
Hotstring("C0")

newVal := false
origVal := A_DefaultHotstringCaseSensitive

AssertEq(origVal, newVal, A_LineNumber)

AssertEq(origVal, A_DefaultHotstringCaseSensitive, A_LineNumber)

AssertEq(newVal, A_DefaultHotstringCaseSensitive, A_LineNumber)


; Inside word.
newVal := true
origVal := A_DefaultHotstringDetectWhenInsideWord

AssertEq(origVal, newVal, A_LineNumber)

#Hotstring ?

AssertEq(origVal, A_DefaultHotstringDetectWhenInsideWord, A_LineNumber)

AssertEq(newVal, A_DefaultHotstringDetectWhenInsideWord, A_LineNumber)
	
; Automatic backspacing off.
newVal := false
origVal := A_DefaultHotstringDoBackspace

AssertEq(origVal, newVal, A_LineNumber)

#Hotstring B0

AssertEq(origVal, A_DefaultHotstringDoBackspace, A_LineNumber)

AssertEq(newVal, A_DefaultHotstringDoBackspace, A_LineNumber)


; Automatic backspacing back on.
Hotstring("B")

newVal := true
origVal := A_DefaultHotstringDoBackspace

AssertEq(origVal, newVal, A_LineNumber)

AssertEq(origVal, A_DefaultHotstringDoBackspace, A_LineNumber)

AssertEq(newVal, A_DefaultHotstringDoBackspace, A_LineNumber)

; Do not conform to typed case.
; Even though directive set to C1, the actual value at this point is C0 because it was
; internally set above when toggling C0
newVal := true
origVal := A_DefaultHotstringConformToCase

AssertEq(origVal, newVal, A_LineNumber)

#hotstring C1
	

AssertEq(origVal, A_DefaultHotstringConformToCase, A_LineNumber)

AssertEq(newVal, A_DefaultHotstringConformToCase, A_LineNumber)

; Omit ending character.
newVal := true
origVal := A_DefaultHotstringOmitEndChar

AssertEq(origVal, newVal, A_LineNumber)
	
#Hotstring O

AssertEq(origVal, A_DefaultHotstringOmitEndChar, A_LineNumber)

AssertEq(newVal, A_DefaultHotstringOmitEndChar, A_LineNumber)

; Restore ending character.
Hotstring("O0")

newVal := false
origVal := A_DefaultHotstringOmitEndChar

AssertEq(origVal, newVal, A_LineNumber)

AssertEq(origVal, A_DefaultHotstringOmitEndChar, A_LineNumber)

AssertEq(newVal, A_DefaultHotstringOmitEndChar, A_LineNumber)

; Exempt from suspend.
newVal := true
origVal := A_SuspendExempt

AssertEq(origVal, newVal, A_LineNumber)

#Hotstring S

AssertEq(origVal, A_SuspendExempt, A_LineNumber)

AssertEq(newVal, A_SuspendExempt, A_LineNumber)


; Remove suspend exempt.
Hotstring("S0")
newVal := false
origVal := A_SuspendExempt

AssertEq(origVal, newVal, A_LineNumber)

AssertEq(origVal, A_SuspendExempt, A_LineNumber)

AssertEq(newVal, A_SuspendExempt, A_LineNumber)

; Reset on trigger.
newVal := true
origVal := A_DefaultHotstringDoReset

AssertEq(origVal, newVal, A_LineNumber)

#Hotstring Z

AssertEq(origVal, A_DefaultHotstringDoReset, A_LineNumber)

AssertEq(newVal, A_DefaultHotstringDoReset, A_LineNumber)

; Restore reset on trigger.
Hotstring("Z0")
newVal := false
origVal := A_DefaultHotstringDoReset

AssertEq(origVal, newVal, A_LineNumber)

AssertEq(origVal, A_DefaultHotstringDoReset, A_LineNumber)

AssertEq(newVal, A_DefaultHotstringDoReset, A_LineNumber)
		

; Send replacement text raw.
newMode := "Raw"
origMode := A_DefaultHotstringSendRaw

AssertEq(origMode, "Raw", A_LineNumber)

#Hotstring R

AssertEq(origMode, A_DefaultHotstringSendRaw, A_LineNumber)

AssertEq(newMode, A_DefaultHotstringSendRaw, A_LineNumber)


; Restore replacement text mode.
Hotstring("R0")
newMode := "NotRaw"
origMode := A_DefaultHotstringSendRaw

AssertEq(origMode, "NotRaw", A_LineNumber)

AssertEq(origMode, A_DefaultHotstringSendRaw, A_LineNumber)

AssertEq(newMode, A_DefaultHotstringSendRaw, A_LineNumber)

; Send replacement text mode.
Hotstring("T")
newMode := "RawText"
origMode := A_DefaultHotstringSendRaw

AssertEq(origMode, "RawText", A_LineNumber)

AssertEq(origMode, A_DefaultHotstringSendRaw, A_LineNumber)

AssertEq(newMode, A_DefaultHotstringSendRaw, A_LineNumber)

; Restore replacement text mode.
Hotstring("T0")
newMode := "NotRaw"
origMode := A_DefaultHotstringSendRaw

AssertEq(origMode, "NotRaw", A_LineNumber)

AssertEq(origMode, A_DefaultHotstringSendRaw, A_LineNumber)

AssertEq(newMode, A_DefaultHotstringSendRaw, A_LineNumber)

; Key delay.
newInt := 42
origInt := A_DefaultHotstringKeyDelay

AssertEq(origInt, 42, A_LineNumber)

#Hotstring K42

AssertEq(origInt, A_DefaultHotstringKeyDelay, A_LineNumber)

AssertEq(newInt, A_DefaultHotstringKeyDelay, A_LineNumber)


; Priority.
newInt := 42
origInt := A_DefaultHotstringPriority

AssertEq(origInt, 42, A_LineNumber)

#Hotstring P42

AssertEq(origInt, A_DefaultHotstringPriority, A_LineNumber)

AssertEq(newInt, A_DefaultHotstringPriority, A_LineNumber)
	
			
; Send mode Event.
newSendMode := "Event"
origSendMode := A_DefaultHotstringSendMode

AssertEq(origSendMode, "Event", A_LineNumber)
	
#Hotstring SE

AssertEq(origSendMode, A_DefaultHotstringSendMode, A_LineNumber)

AssertEq(newSendMode, A_DefaultHotstringSendMode, A_LineNumber)

; Send mode Play.
Hotstring("SP")
newSendMode := "Play"
origSendMode := A_DefaultHotstringSendMode

AssertEq(origSendMode, "Play", A_LineNumber)

AssertEq(origSendMode, A_DefaultHotstringSendMode, A_LineNumber)

AssertEq(newSendMode, A_DefaultHotstringSendMode, A_LineNumber)
; Send mode Input.
Hotstring("SI")
newSendMode := "Input"
origSendMode := A_DefaultHotstringSendMode

AssertEq(origSendMode, "InputThenPlay", A_LineNumber)

AssertEq(origSendMode, A_DefaultHotstringSendMode, A_LineNumber)

AssertEq("InputThenPlay", A_DefaultHotstringSendMode, A_LineNumber)

FileAppend "pass", "*"

ExitApp()
