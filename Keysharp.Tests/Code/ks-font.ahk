; Ks.Font: the value object behind Gui.Font / Gui.Control.Font.
#ErrorStdOut
#Warn All, StdOut
#import KS { Font, Image }

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

; A numeric colour is masked to 24 bits rather than letting a high byte reach the alpha.
num := Font()
num.Color := 0x12345678
Check("numeric color", num.Color, "345678")

; ---- the platform's well-known fonts -------------------------------------------------------------
Check("Ui has a name", Font.Ui.Name != "", true)
Check("Emoji has a name", Font.Emoji.Name != "", true)
Check("GuiDefault has a name", Font.GuiDefault.Name != "", true)
Check("Monospace has a name", Font.Monospace.Name != "", true)

; Exists answers for a real family and a made-up one.
Check("Exists(Ui)", Font.Exists(Font.Ui.Name), true)
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
g := Gui()
g.SetFont("s14 bold", "Verdana")
gf := g.Font
Check("gui name", gf.Name, "Verdana")
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
Check("assigned name kept", g.Font.Name, "Verdana")

; A font carrying nothing but a family changes the family and leaves the rest alone.
g.SetFont("s20 bold italic", "Arial")
g.Font := Font(, "Verdana")
Check("family-only name", g.Font.Name, "Verdana")
Check("family-only size kept", g.Font.Size, 20)
Check("family-only bold kept", g.Font.Bold, true)
Check("family-only italic kept", g.Font.Italic, true)

; Control-level, including the colour that rides on ForeColor.
ctl := g.Add("Text", "w120", "probe")
ctl.Font := Font("s16 italic cBlue", "Georgia")
cf := ctl.Font
Check("ctl name", cf.Name, "Georgia")
Check("ctl size", cf.Size, 16)
Check("ctl italic", cf.Italic, true)
Check("ctl color", cf.Color, "0000FF")

; Assigning something that is not a Font must raise.
Rejects("gui bad assign", () => g.Font := "s12")
Rejects("ctl bad assign", () => ctl.Font := 5)

; ---- Image text calls accept a Font in either slot -----------------------------------------------
img := Image.Create(40, 20)
sized := img.MeasureText("Wg", Font("s20", "Arial"))
small := img.MeasureText("Wg", Font("s8", "Arial"))
Check("font object sizes text", sized.Width > small.Width, true)

; The option-string form and the object form must agree.
viaString := img.MeasureText("Wg", "s20", "Arial")
Check("object matches string", sized.Width, viaString.Width)

; A Font in the name slot contributes only its family.
byName := img.MeasureText("Wg", "s20", Font(, "Arial"))
Check("font as name only", byName.Width, viaString.Width)

; A Font carrying a colour must not raise the way a "cRRGGBB" option string does.
img.DrawText("hi", 0, 0, , Font("s10 cFF0000", "Arial"))
Rejects("colour option still rejected", () => img.MeasureText("Wg", "s10 cFF0000", "Arial"))
img.Dispose()

; This file reports value diffs rather than line numbers, so it writes its own accumulated failure text.
if (failed != 0)
	FileAppend("fail: " failed " check(s):" report, "*")

FileAppend "pass", "*"
