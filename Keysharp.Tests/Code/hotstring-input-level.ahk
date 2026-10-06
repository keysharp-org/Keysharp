#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#Import Ks { A_DefaultHotstringSendMode, A_DefaultHotstringSendRaw }
#Include <assert>

#InputLevel 3
::initial::ok
#InputLevel 4
::fallback::ok
#Hotstring I15SIR
::directiveDefault::ok
:I2SP:explicitPlay::ok
#InputLevel 6
::shadowedFallback::ok
#Hotstring I17S0SE
::directiveEvent::ok
:I0SR:explicitZero::ok
:i100si:explicitMaximum::ok
#Hotstring SI
::modeOnly::ok
#Hotstring I0
#InputLevel 7
::zeroDefault::ok
#Hotstring I8
::finalDirective::ok

Hotstring("::dynamicDirective", "ok", "Off")

Hotstring("I23SIR")
Hotstring("::dynamicDefault", "ok", "Off")
AssertEq(A_DefaultHotstringSendMode, "InputThenPlay", A_LineNumber)
AssertEq(A_DefaultHotstringSendRaw, "Raw", A_LineNumber)
Throws(() => Hotstring("I40S R0 SE I101"), A_LineNumber, ValueError)
AssertEq(A_DefaultHotstringSendMode, "InputThenPlay", A_LineNumber)
AssertEq(A_DefaultHotstringSendRaw, "Raw", A_LineNumber)
Hotstring("::dynamicAfterFailure", "ok", "Off")
Hotstring("SI")
Hotstring("::dynamicModeOnly", "ok", "Off")

Hotstring(":I41:dynamicInvalid", "original", "Off")
Throws(() => Hotstring(":*SRI101:dynamicInvalid", "corrupted"), A_LineNumber, ValueError)
Hotstring(":I41:dynamicPreserved", "original", "Off")
Hotstring("I99")
Hotstring("::dynamicPreserved", "updated", "Off")
Hotstring(":I42:dynamicReset", "original", "Off")
Hotstring(":I0:dynamicReset")

for option in ["I", "I-1", "I101", "I1.5", "I999999999999999999999999"]
{
    Throws(() => Hotstring(option), A_LineNumber, ValueError)
    Throws(() => Hotstring(":" . option . ":rejected", "ok", "Off"), A_LineNumber, ValueError)
}

for option in ["Q", "@", "0", "B2", "C2", "S2", "*2", "?2", "O2", "R2", "T2", "X2", "Z2", "K", "P", "K+", "P-", "K1.5", "P1.5", "K1e2", "P1e2", "K999999999999999999999999", "P999999999999999999999999"]
{
    Throws(() => Hotstring("I40S R0 SE " . option), A_LineNumber, ValueError)
    AssertEq(A_DefaultHotstringSendMode, "InputThenPlay", A_LineNumber)
    AssertEq(A_DefaultHotstringSendRaw, "Raw", A_LineNumber)
    Throws(() => Hotstring(":" . option . ":rejected", "ok", "Off"), A_LineNumber, ValueError)
    Throws(() => Hotstring(":" . option . ":dynamicInvalid", "corrupted"), A_LineNumber, ValueError)
}

Hotstring(":I1SI:mixInputAfter", "ok", "Off")
Hotstring(":SII2:mixInputBefore", "ok", "Off")
Hotstring(":I3SP:mixPlayAfter", "ok", "Off")
Hotstring(":SPI4:mixPlayBefore", "ok", "Off")
Hotstring(":I5SE:mixEventAfter", "ok", "Off")
Hotstring(":SEI6:mixEventBefore", "ok", "Off")
Hotstring(":I7S:mixSuspendAfter", "ok", "Off")
Hotstring(":S I8:mixSuspendBefore", "ok", "Off")
Hotstring(":I9R:mixRawAfter", "ok", "Off")
Hotstring(":RI10:mixRawBefore", "ok", "Off")
Hotstring(":I+1:mixSigned", "ok", "Off")
Hotstring(":I11I12:mixLastLevel", "ok", "Off")
Hotstring("I100")
Hotstring("::dynamicMaximum", "ok", "Off")
Hotstring("I0")
Hotstring("::dynamicZero", "ok", "Off")

Hotstring("*1?1B1C0O1R1S1T1X1Z1K+1P-1")
Hotstring("`t*0 ?0 B0 C1 O0 R0 S0 T0 X0 Z0 K-1 P+0")
Hotstring("*? B C O R S T X Z")

FileAppend "pass", "*"
ExitApp()
