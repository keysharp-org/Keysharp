; Ks.Font: the value object behind Gui.Font / Gui.Control.Font.
#ErrorStdOut
#Warn All, StdOut
#Warn Experimental, Off
#import KS { Font, Image }

#CSharp
public static string FontTestCulture(string name)
{
	var previous = System.Globalization.CultureInfo.CurrentCulture.Name;
	System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(name);
	return previous;
}
#EndCSharp

failed := 0
report := ""

Check(label, actual, expected)
{
	global failed
	if (actual != expected)
	{
		failed++
		global report
		report .= " [" label ": got <" actual "> want <" expected ">]"
	}
}

Rejects(label, fn)
{
	global failed
	try
	{
		fn()
		failed++
		global report
		report .= " [" label ": expected a throw]"
	}
	catch
	{
	}
}

; ---- construction takes SetFont's two arguments, in SetFont's order ------------------------------
f := Font("s12 bold italic cRed", "Consolas")
Check("ctor name", f.Name, "Consolas")
Check("ctor size", f.Size, 12)
Check("ctor bold", f.Bold, true)
Check("ctor weight", f.Weight, 700)
Check("ctor italic", f.Italic, true)
Check("ctor color", f.Color, "FF0000")

; ---- every property is optional and reads back as "" ---------------------------------------------
bare := Font(, "Arial")
Check("bare name", bare.Name, "Arial")
Check("bare size", bare.Size, "")
Check("bare color", bare.Color, "")
Check("bare weight", bare.Weight, "")
Check("bare bold", bare.Bold, "")
Check("bare italic", bare.Italic, "")
Check("bare underline", bare.Underline, "")
Check("bare strike", bare.Strike, "")
Check("bare options", bare.Options, "")

; Reading an unset property must never raise, including inside a concatenation.
Check("unset concat", "<" bare.Size "|" bare.Weight ">", "<|>")

; Writing "" clears again, so the marker is the same in both directions.
bare.Size := 11
Check("set size", bare.Size, 11)
bare.Size := ""
Check("clear size", bare.Size, "")

; ---- Options emits only what is set --------------------------------------------------------------
Check("options size only", Font("s10").Options, "s10")
Check("options bold", Font("bold").Options, "w700")
Check("options italic", Font("italic").Options, "italic")

; "norm" is the only way to switch a style off, so it leads when one is explicitly off - but a plain
; weight must NOT drag it in, or applying the font would clear styles it never set.
Check("options norm", Font("norm").Options, "norm")
b := Font()
b.Bold := false
Check("options bold-off", b.Options, "w400")
w := Font()
w.Weight := 500
Check("options mid weight", w.Options, "w500")

; ---- fractional sizes round-trip exactly ---------------------------------------------------------
frac := Font()
frac.Size := 10.1
Check("fractional size", frac.Size, 10.1)
Check("fractional options", frac.Options, "s10.1")
previousCulture := FontTestCulture("de-DE")
try
	Check("fractional options across locales", Font(frac.Options).Size, 10.1)
finally
	FontTestCulture(previousCulture)

; ---- booleans take the spellings the rest of the API takes ---------------------------------------
sp := Font()
sp.Bold := "1"
Check("bool '1'", sp.Bold, true)
sp.Bold := "true"
Check("bool 'true'", sp.Bold, true)
sp.Bold := "on"
Check("bool 'on'", sp.Bold, true)

; ---- invalid values raise rather than coercing to zero -------------------------------------------
bad := Font()
bad.Size := 12
Rejects("bad size", () => bad.Size := "abc")
Check("bad size left alone", bad.Size, 12)
Rejects("bad weight", () => bad.Weight := "abc")
Rejects("bad color", () => bad.Color := "NotAColour")
Rejects("unknown constructor option", () => Font("italik"))
Rejects("malformed constructor size", () => Font("sabc"))
Rejects("malformed constructor color", () => Font("cNotAColour"))
Rejects("zero size", () => bad.Size := 0)
Rejects("negative size", () => bad.Size := -1)
Rejects("size underflows native float", () => bad.Size := 1.0e-300)
Rejects("NaN size", () => bad.Size := "NaN")
Rejects("infinite size", () => bad.Size := "Infinity")
Rejects("fractional weight", () => bad.Weight := 500.9)
Rejects("weight below range", () => bad.Weight := 0)
Rejects("weight above range", () => bad.Weight := 1001)
Rejects("quality below range", () => bad.Quality := -1)
Rejects("quality above range", () => bad.Quality := 6)
Rejects("fractional quality", () => bad.Quality := 1.5)
Check("invalid setters preserve size", bad.Size, 12)

alpha := Font()
alpha.Color := "80FF0000"
opaque := Font()
opaque.Color := alpha.Color
Check("color copy preserves equality", alpha = opaque, true)

; A numeric colour is masked to 24 bits rather than letting a high byte reach the alpha.
num := Font()
num.Color := 0x12345678
Check("numeric color", num.Color, "345678")

; ---- the platform's well-known fonts -------------------------------------------------------------
Check("UiDefault has a name", Font.UiDefault.Name != "", true)
Check("Emoji has a name", Font.Emoji.Name != "", true)
Check("GuiDefault has a name", Font.GuiDefault.Name != "", true)
Check("Monospace has a name", Font.Monospace.Name != "", true)

; Exists answers for a real family and a made-up one.
Check("Exists(UiDefault)", Font.Exists(Font.UiDefault.Name), true)
Check("Exists(nonsense)", Font.Exists("No Such Family At All"), false)
Check("Exists('')", Font.Exists(""), false)
Check("Families is an Array", Font.Families is Array, true)
Check("Families is populated", Font.Families.Length > 0, true)

; ---- value equality ------------------------------------------------------------------------------
Check("equal fonts", Font("s10 bold", "Arial") = Font("s10 bold", "Arial"), true)
Check("different size", Font("s10", "Arial") = Font("s11", "Arial"), false)
Check("different family", Font("s10", "Arial") = Font("s10", "Verdana"), false)
Check("family case-insensitive", Font("s10", "Arial") = Font("s10", "ARIAL"), true)
Check("unset differs from set", Font("s10", "Arial") = Font(, "Arial"), false)

; ---- Clone copies the font's own state -----------------------------------------------------------
orig := Font("s10", "Arial")
copy := orig.Clone()
copy.Size := 99
Check("clone independent", orig.Size, 10)
Check("clone took the value", copy.Size, 99)

; ---- subclassing ---------------------------------------------------------------------------------
class MonoFont extends Font
{
	static Made := 0
	__New(size := 10)
	{
		super.__New("s" size, "Consolas")
		MonoFont.Made += 1
	}
	Describe() => this.Name " " this.Options
}

mf := MonoFont(14)
Check("subclass name", mf.Name, "Consolas")
Check("subclass options", mf.Options, "s14")
Check("subclass method", mf.Describe(), "Consolas s14")
Check("subclass __New ran once", MonoFont.Made, 1)
Check("subclass is a Font", mf is Font, true)
mf.Italic := true
Check("subclass inherits setters", mf.Options, "s14 italic")

; ---- Gui.Font / Gui.Control.Font round-trip ------------------------------------------------------
guiFamily := Font.UiDefault.Name
controlFamily := Font.Monospace.Name
g := Gui()
g.SetFont("s14 bold", guiFamily)
gf := g.Font
Check("gui name", gf.Name, guiFamily)
Check("gui size", gf.Size, 14)
Check("gui bold", gf.Bold, true)

; Reading returns a detached copy: mutating it must not touch the Gui.
gf.Size := 40
Check("snapshot detached", g.Font.Size, 14)

; Assigning applies it.
gf2 := g.Font
gf2.Size := 18
gf2.Bold := false
g.Font := gf2
Check("assigned size", g.Font.Size, 18)
Check("assigned bold", g.Font.Bold, false)
Check("assigned name kept", g.Font.Name, guiFamily)

; A font carrying nothing but a family changes the family and leaves the rest alone.
g.SetFont("s20 bold italic", controlFamily)
g.Font := Font(, guiFamily)
Check("family-only name", g.Font.Name, guiFamily)
Check("family-only size kept", g.Font.Size, 20)
Check("family-only bold kept", g.Font.Bold, true)
Check("family-only italic kept", g.Font.Italic, true)

partial := Font()
partial.Italic := false
Check("partial Options still emits norm", partial.Options, "norm")
g.SetFont("bold italic underline strike")
g.Font := partial
Check("partial clears italic", g.Font.Italic, false)
Check("partial preserves bold", g.Font.Bold, true)
Check("partial preserves underline", g.Font.Underline, true)
Check("partial preserves strike", g.Font.Strike, true)

g.SetFont("norm s12", guiFamily)
g.Font := Font("s20 bold", "No Such Family At All")
Check("missing family preserves current name", g.Font.Name, guiFamily)
Check("missing family applies size", g.Font.Size, 20)
Check("missing family applies bold", g.Font.Bold, true)

g.SetFont(Font("s18 bold", controlFamily))
Check("SetFont object name", g.Font.Name, controlFamily)
Check("SetFont object size", g.Font.Size, 18)
g.SetFont(Font("s16", controlFamily), guiFamily)
Check("SetFont explicit name wins", g.Font.Name, guiFamily)
Rejects("SetFont rejects Font in name slot", () => g.SetFont("s12", Font()))

g.SetFont("cRed cBlue")
Check("string color last wins", g.Font.Color, "0000FF")
g.SetFont(Font("cRed cBlue"))
Check("object color last wins", g.Font.Color, "0000FF")

; Control-level, including the colour that rides on ForeColor.
ctl := g.Add("Text", "w120", "probe")
ctl.Font := Font("s16 italic cBlue", controlFamily)
cf := ctl.Font
Check("ctl name", cf.Name, controlFamily)
Check("ctl size", cf.Size, 16)
Check("ctl italic", cf.Italic, true)
Check("ctl color", cf.Color, "0000FF")
ctl.SetFont("bold italic underline strike")
ctl.Font := partial
Check("control partial clears italic", ctl.Font.Italic, false)
Check("control partial preserves bold", ctl.Font.Bold, true)
Check("control partial preserves underline", ctl.Font.Underline, true)
Check("control partial preserves strike", ctl.Font.Strike, true)
ctl.SetFont(Font("s15", guiFamily))
Check("control SetFont object name", ctl.Font.Name, guiFamily)
Check("control SetFont object size", ctl.Font.Size, 15)

#if WINDOWS
; Repeated FontHandle reads reuse the GUI's cached native font.
g.SetFont("s12 norm", guiFamily)
handle := g.FontHandle
Check("FontHandle nonzero", handle != 0, true)
Check("FontHandle stable", g.FontHandle, handle)
g.SetFont("cRed")
Check("color-only update preserves FontHandle", g.FontHandle, handle)
nativeFont := Buffer(92, 0)
Check("FontHandle is a font", DllCall("gdi32\GetObjectW", "Ptr", handle, "Int", nativeFont.Size, "Ptr", nativeFont, "Int"), 92)
oldHeight := Abs(NumGet(nativeFont, 0, "Int"))
g.SetFont("s24 bold")
Check("FontHandle refreshes", g.FontHandle != handle, true)
DllCall("gdi32\GetObjectW", "Ptr", g.FontHandle, "Int", nativeFont.Size, "Ptr", nativeFont, "Int")
Check("FontHandle reflects bold", NumGet(nativeFont, 16, "Int"), 700)
Check("FontHandle reflects size", Abs(NumGet(nativeFont, 0, "Int")), oldHeight * 2)
g.SetFont("q3")
Check("GUI snapshot reflects quality", g.Font.Quality, 3)
qualityControl := g.Add("Text",, "quality")
Check("control inherits quality", qualityControl.Font.Quality, 3)
appliedHandle := DllCall("user32\SendMessageW", "Ptr", qualityControl.Hwnd, "UInt", 0x31, "Ptr", 0, "Ptr", 0, "Ptr")
Check("control has native font", appliedHandle != 0, true)
DllCall("gdi32\GetObjectW", "Ptr", appliedHandle, "Int", nativeFont.Size, "Ptr", nativeFont, "Int")
Check("native control font reflects quality", NumGet(nativeFont, 26, "UChar"), 3)
Rejects("FontHandle read only", () => g.FontHandle := 0)
#endif

; Assigning something that is not a Font must raise.
Rejects("gui bad assign", () => g.Font := "s12")
Rejects("ctl bad assign", () => ctl.Font := 5)

; ---- Image text calls accept a Font in the options slot ------------------------------------------
img := Image.Create(40, 20)
sized := img.MeasureText("Wg", Font("s20", "Arial"))
small := img.MeasureText("Wg", Font("s8", "Arial"))
Check("font object sizes text", sized.Width > small.Width, true)

; The option-string form and the object form must agree.
viaString := img.MeasureText("Wg", "s20", "Arial")
Check("object matches string", sized.Width, viaString.Width)

Rejects("image rejects Font in name slot", () => img.MeasureText("Wg", "s20", Font(, "Arial")))

; A Font carrying a colour must not raise the way a "cRRGGBB" option string does.
img.DrawText("hi", 0, 0, , Font("s10 cFF0000", "Arial"))
Rejects("colour option still rejected", () => img.MeasureText("Wg", "s10 cFF0000", "Arial"))
img.Dispose()

#if WINDOWS
pixelCounts(quality, previousQuality := "") {
	canvas := Image.Create(80, 40, "White")
	if (previousQuality != "")
		canvas.DrawText("Wg", 40, 0, "Black", "s18 q" previousQuality, "Arial")
	canvas.DrawText("Wg", 0, 0, "Black", "s18" (quality = "" ? "" : " q" quality), "Arial")
	gray := 0
	black := 0
	Loop 40 {
		y := A_Index - 1
		Loop 40 {
			pixel := canvas.GetPixel(A_Index - 1, y) & 0xFFFFFF
			if (pixel = 0)
				black++
			else if (pixel != 0xFFFFFF)
				gray++
		}
	}
	canvas.Dispose()
	return {Gray: gray, Black: black}
}
aliased := pixelCounts(3)
Check("q3 paints text", aliased.Black > 0, true)
Check("q3 disables antialiasing", aliased.Gray, 0)
Check("q4 enables antialiasing", pixelCounts(4).Gray > 0, true)
Check("default quality resets previous draw", pixelCounts("", 3).Gray, pixelCounts("").Gray)
lastHandle := g.FontHandle
g.Destroy()
Check("FontHandle released with GUI", DllCall("gdi32\GetObjectW", "Ptr", lastHandle, "Int", nativeFont.Size, "Ptr", nativeFont, "Int"), 0)
#else
g.Destroy()
#endif

; This file reports value diffs rather than line numbers, so it writes its own accumulated failure text.
if (failed != 0)
	FileAppend("fail: " failed " check(s):" report, "*")

FileAppend "pass", "*"
