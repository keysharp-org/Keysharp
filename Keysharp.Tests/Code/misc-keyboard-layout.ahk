#ErrorStdOut
#Warn All, StdOut
#NoTrayIcon
#import KS { GetKeyboardLayout, GetKeyInfo }
#Include <assert>

layout := GetKeyboardLayout()

Assert(Trim(layout) != "", A_LineNumber)

; Named keys and control characters resolve the same way on every layout.
newline := GetKeyInfo("`n")

Assert(newline is Object, A_LineNumber)

AssertEq(newline.Name, "Enter", A_LineNumber)

AssertEq(newline.Prefix, "", A_LineNumber)

esc := GetKeyInfo("Esc")

Assert(esc is Object, A_LineNumber)

AssertEq(esc.VK, GetKeyVK("Esc"), A_LineNumber)

AssertEq(esc.SC, GetKeySC("Esc"), A_LineNumber)

; A letter only resolves on a layout which can type it.
if GetKeyVK("a")
{
	lower := GetKeyInfo("a")

	Assert(lower is Object, A_LineNumber)

	Assert(lower.VK > 0 && lower.Name != "" && HasProp(lower, "Prefix"), A_LineNumber)

	upper := GetKeyInfo("A")

	Assert(upper is Object, A_LineNumber)

	Assert((upper.Modifiers & 4) && InStr(upper.Prefix, "+"), A_LineNumber)

	Assert(GetKeyInfo("a", layout) is Object, A_LineNumber)
}

FileAppend "pass", "*"
