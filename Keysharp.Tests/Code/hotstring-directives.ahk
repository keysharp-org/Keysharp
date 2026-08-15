#NoTrayIcon

#import KS { * }
#Hotstring NoMouse

if (A_DefaultHotstringNoMouse)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"


; Reset to what it was for the sake of other tests in this class.
Hotstring("MouseReset", true)

#Hotstring EndChars -()[]{}':;"/\,.?!`n`s`t

if (A_DefaultHotstringEndChars == "-()[]{}':;`"/\,.?!`n`s`t")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	

; End char required.
newVal := false
origVal := A_DefaultHotstringEndCharRequired

if (origVal == newVal)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
		
#hotstring * ; Comes after the test above, but actually gets executed before
Hotstring("*0")

if (origVal != A_DefaultHotstringEndCharRequired)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (A_DefaultHotstringEndCharRequired)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	

; Case sensitivity. Will be false by default even though the directive sets it to true,
; because it will have been internally toggled because of the call to C1 below.
newVal := false
origVal := A_DefaultHotstringCaseSensitive

if (origVal == newVal)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

#Hotstring C ; Runs before test above.

if (origVal == A_DefaultHotstringCaseSensitive)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newVal == A_DefaultHotstringCaseSensitive)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Case sensitivity restore to default.
Hotstring("C0")

newVal := false
origVal := A_DefaultHotstringCaseSensitive

if (origVal == newVal)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (origVal == A_DefaultHotstringCaseSensitive)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newVal == A_DefaultHotstringCaseSensitive)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"


; Inside word.
newVal := true
origVal := A_DefaultHotstringDetectWhenInsideWord

if (origVal == newVal)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

#Hotstring ?

if (origVal == A_DefaultHotstringDetectWhenInsideWord)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newVal == A_DefaultHotstringDetectWhenInsideWord)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
; Automatic backspacing off.
newVal := false
origVal := A_DefaultHotstringDoBackspace

if (origVal == newVal)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

#Hotstring B0

if (origVal == A_DefaultHotstringDoBackspace)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newVal == A_DefaultHotstringDoBackspace)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"


; Automatic backspacing back on.
Hotstring("B")

newVal := true
origVal := A_DefaultHotstringDoBackspace

if (origVal == newVal)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (origVal == A_DefaultHotstringDoBackspace)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newVal == A_DefaultHotstringDoBackspace)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Do not conform to typed case.
; Even though directive set to C1, the actual value at this point is C0 because it was
; internally set above when toggling C0
newVal := true
origVal := A_DefaultHotstringConformToCase

if (origVal == newVal)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

#hotstring C1
	

if (origVal == A_DefaultHotstringConformToCase)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newVal == A_DefaultHotstringConformToCase)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Omit ending character.
newVal := true
origVal := A_DefaultHotstringOmitEndChar

if (origVal == newVal)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
#Hotstring O

if (origVal == A_DefaultHotstringOmitEndChar)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newVal == A_DefaultHotstringOmitEndChar)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Restore ending character.
Hotstring("O0")

newVal := false
origVal := A_DefaultHotstringOmitEndChar

if (origVal == newVal)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (origVal == A_DefaultHotstringOmitEndChar)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newVal == A_DefaultHotstringOmitEndChar)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Exempt from suspend.
newVal := true
origVal := A_SuspendExempt

if (origVal == newVal)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

#Hotstring S

if (origVal == A_SuspendExempt)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newVal == A_SuspendExempt)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"


; Remove suspend exempt.
Hotstring("S0")
newVal := false
origVal := A_SuspendExempt

if (origVal == newVal)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (origVal == A_SuspendExempt)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newVal == A_SuspendExempt)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Reset on trigger.
newVal := true
origVal := A_DefaultHotstringDoReset

if (origVal == newVal)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

#Hotstring Z

if (origVal == A_DefaultHotstringDoReset)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newVal == A_DefaultHotstringDoReset)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Restore reset on trigger.
Hotstring("Z0")
newVal := false
origVal := A_DefaultHotstringDoReset

if (origVal == newVal)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (origVal == A_DefaultHotstringDoReset)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newVal == A_DefaultHotstringDoReset)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
		

; Send replacement text raw.
newMode := "Raw"
origMode := A_DefaultHotstringSendRaw

if (origMode == "Raw")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

#Hotstring R

if (origMode == A_DefaultHotstringSendRaw)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newMode == A_DefaultHotstringSendRaw)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"


; Restore replacement text mode.
Hotstring("R0")
newMode := "NotRaw"
origMode := A_DefaultHotstringSendRaw

if (origMode == "NotRaw")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (origMode == A_DefaultHotstringSendRaw)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newMode == A_DefaultHotstringSendRaw)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Send replacement text mode.
Hotstring("T")
newMode := "RawText"
origMode := A_DefaultHotstringSendRaw

if (origMode == "RawText")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (origMode == A_DefaultHotstringSendRaw)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newMode == A_DefaultHotstringSendRaw)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Restore replacement text mode.
Hotstring("T0")
newMode := "NotRaw"
origMode := A_DefaultHotstringSendRaw

if (origMode == "NotRaw")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (origMode == A_DefaultHotstringSendRaw)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newMode == A_DefaultHotstringSendRaw)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Key delay.
newInt := 42
origInt := A_DefaultHotstringKeyDelay

if (origInt == 42)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

#Hotstring K42

if (origInt == A_DefaultHotstringKeyDelay)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newInt == A_DefaultHotstringKeyDelay)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"


; Priority.
newInt := 42
origInt := A_DefaultHotstringPriority

if (origInt == 42)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

#Hotstring P42

if (origInt == A_DefaultHotstringPriority)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newInt == A_DefaultHotstringPriority)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
			
; Send mode Event.
newSendMode := "Event"
origSendMode := A_DefaultHotstringSendMode

if (origSendMode == "Event")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
#Hotstring SE

if (origSendMode == A_DefaultHotstringSendMode)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newSendMode == A_DefaultHotstringSendMode)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; Send mode Play.
Hotstring("SP")
newSendMode := "Play"
origSendMode := A_DefaultHotstringSendMode

if (origSendMode == "Play")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (origSendMode == A_DefaultHotstringSendMode)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (newSendMode == A_DefaultHotstringSendMode)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
; Send mode Input.
Hotstring("SI")
newSendMode := "Input"
origSendMode := A_DefaultHotstringSendMode

if (origSendMode == "InputThenPlay")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (origSendMode == A_DefaultHotstringSendMode)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if ("InputThenPlay" == A_DefaultHotstringSendMode)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

ExitApp()