#import "Ks" { * }
; OCR library (cross-platform). Relative path from Code/Gui to Keysharp/Scripts.
#include ../../../Keysharp/Scripts/OCR.ks
If (FileExist(A_Desktop . "/MyScreenClip.png"))
	FileDelete(A_Desktop . "/MyScreenClip.png")

GuiBGColor := "FF9A9A"
;BGColor2 := "0xFFFFAA"

Gui2 := ""

; Second GUI



; ┌───────────┐
; │  Globals  │
; └───────────┘

global gb3Hwnd, gui2StyleButtonHwnd

; --- Globals for the merged manual-suite probes (input / window-capture / clipboard / OCR / WinEvent + shared log) ---
global gLogEdit := ""
global gLogText := ""
global gStatus := Map()
global gSendTarget := ""
global gHotstringTarget := ""
global gInputHookExpected := ""
global gInputHookActual := ""
global gWindowTitleEdit := ""
global gWindowInfoEdit := ""
global gClipboardTextEdit := ""
global gOcrResultEdit := ""
global gClipboardMonitorEnabled := false
global gClipboardChangeCount := 0
global gClipboardClipWaitRunning := false
global gClipboardDelayedPayload := ""
global gHotkeyHitCount := 0
global gHotstringHitCount := 0
global gInputHookObj := ""
global gInputHookLastText := ""
global gInputHookLastReason := ""
global gWinEventHooks := []
global gWinEventCount := 0
global gWinEventMoveCount := 0
global gWinEventCaretCount := 0
global gMouseHookObj := ""
global gMouseDownCount := 0
global gMouseUpCount := 0
global gMouseMoveCount := 0
global gPixelAssetPath := A_ScriptDir "/killbill.png"
global pixelSwatch := ""

winposx := ""
winposy := ""
winposw := ""
winposh := ""
origBackColor := ""
LVFolder := A_MyDocuments

; ┌────────────────┐
; │  Tab One Menu  │
; └────────────────┘
FileMenu := Menu()
FileMenu.Add("&System", "MenuHandler")
FileMenu.Add("S&cript Icon", "MenuHandler")
FileMenu.Add("S&uspend Icon", "MenuHandler")
FileMenu.Add("&Pause Icon", "MenuHandler")
#if WINDOWS
	FileMenu.SetIcon("&System", "Shell32.dll", 174) ; 2nd icon group from the file
#endif
FileMenu.SetIcon("S&cript Icon", A_KeysharpCorePath, "Keysharp.ico")
FileMenu.SetIcon("S&uspend Icon", A_KeysharpCorePath, "Keysharp_s.ico")
FileMenu.SetIcon("&Pause Icon", A_KeysharpCorePath, "Keysharp_p.ico")

; Create another menu destined to become a submenu of the above menu.
MainSubmenu1 := Menu()
MainSubmenu1.Add("Item &A", "MenuHandler")
MainSubmenu1.Add("Item &B", "MenuHandler")

; Create a submenu in the first menu (a right-arrow indicator). When the user selects it, the second menu is displayed.
FileMenu.Add("My Su&bmenu", MainSubmenu1)

ImgSrchMenu := Menu()
ImgSrchMenu.Add("&Image Search Test", "ImgSrch")

; Menu presentation options added in v2.1:
;   Radio changes a checked item's mark from a tick to a bullet.
;   RTL changes the item's text/layout direction.
;   Break and BarBreak begin new popup-menu columns (BarBreak also draws a divider).
;   Right applies to a top-level menu-bar item, so this whole menu sits at the right edge.
; Selecting an item never changes its own mark on its own -- AHK leaves that to the script -- so the marks
; here are wired up to be checked by hand: the three radio items act as one group where picking any of them
; moves the bullet, and the tick item turns its checkmark on and off.
PresentationMenu := Menu()
PresentationMenu.Add("Radio choice &1", "PickPresentationRadio", "Radio")
PresentationMenu.Add("Radio choice &2", "PickPresentationRadio", "Radio")
PresentationMenu.Add("Radio choice &3", "PickPresentationRadio", "Radio")
PresentationMenu.Check("Radio choice &1")
PresentationMenu.Add()  ; Add a separator line.
PresentationMenu.Add("Toggle my chec&kmark", "TogglePresentationCheck")
PresentationMenu.Check("Toggle my chec&kmark")
PresentationMenu.Add("RTL sample: שלום", "MenuHandler", "RTL")
PresentationMenu.Add("Break: column two", "MenuHandler", "Break")
PresentationMenu.Add("Second-column item", "MenuHandler")
PresentationMenu.Add("BarBreak: column three", "MenuHandler", "BarBreak")
PresentationMenu.Add("Third-column item", "MenuHandler")

MyMenuBar := MenuBar()
MyMenuBar.Add("&Menu Icon Test", FileMenu)
MyMenuBar.Add("&Image Search", ImgSrchMenu)
MyMenuBar.Add("&Presentation", PresentationMenu, "Right")

MyGui := Gui(, "KEYSHARP TESTS")
MyGui.OnEvent("Close", "CloseApp")
#if WINDOWS
OnMessage(0x0233, "WM_DROPFILES_CZ")
#endif

CloseApp(*) {
#if WINDOWS
	global shell := ""
#endif
	ExitApp
}

; ┌───────────────────┐
; │  Add Menu to GUI  │
; └───────────────────┘

MyGui.MenuBar := MyMenuBar

; ┌──────────────┐
; │  Status Bar  │
; └──────────────┘
MySB := MyGui.Add("StatusBar", "h36", "                       ")

; ┌─────────────┐
; │  Start TAB  │
; └─────────────┘

Tab := MyGui.Add("Tab3", , ["Lists, Menus && Styles", "Edits && Messages", "Pickers && Sliders", "ControlZoo", "Send && Hotkey", "Dll && COM", "Image", "Windows", "Clipboard", "Sound"])

Tab.UseTab("Lists, Menus & Styles")

; ┌──────────────────────┐
; │  Create the window:  │
; └──────────────────────┘

MyGui.SetFont("cBlack s8", "Arial")
TEST_HEADER := MyGui.Add("Text", "s20 w1200","Keysharp GUI Tests")

; ┌────────────────────────────────────┐
; │  Add button to change header font  │
; └────────────────────────────────────┘
headerBtn := MyGui.Add("Button", "s8 xc+10 y+10", "Make header font larger Comic Sans MS")
headerBtn.OnEvent("Click", "ChangeFont")

; ┌──────────────────────────────┐
; │  Add button to restore font  │
; └──────────────────────────────┘
headerBtn2 := MyGui.Add("Button", "s8 x+10 yp", "Restore header font")
headerBtn2.OnEvent("Click", "ChangeFontBack")

; ┌───────────────────────────────────┐
; │  Add button to change background  │
; └───────────────────────────────────┘
bgBtn := MyGui.Add("Button", "s8 x+10 yp", "Change GUI Backgroud")
bgBtn.OnEvent("Click", "ChangeBG")

; ┌────────────────────────────────────┐
; │  Add button to restore background  │
; └────────────────────────────────────┘
bgBtn2 := MyGui.Add("Button", "s8 x+10 yp", "Restore GUI Backgroud")
bgBtn2.OnEvent("Click", "RestoreBG")

; ┌─────────────────┐
; │  GroupBox test  │
; └─────────────────┘

gb1_TabOne := MyGui.Add("GroupBox", "xc+10 y+10 w325", "Tab One - Group One") ;
MyGui.UseGroup(gb1_TabOne)

; ┌──────────────────────────────────┐
; │  Listview testing                │
; │  Double-click activates tooltip  │
; └──────────────────────────────────┘
LV_Label := MyGui.Add("Text", "w300 h20 xc+10 y+20","Create listview with tooltip - double-click row")
LV_Label.SetFont("cBlue s10")
; Create the ListView with two columns, Name and Size:
LV := MyGui.Add("ListView", "r9 w300 xc+10 y+5 BackgroundTeal", ["Name","Size (KB)"])

; ┌────────────────────────────────────────────────────────────┐
; │  Notify the script whenever the user double clicks a row:  │
; └────────────────────────────────────────────────────────────┘
LV.OnEvent("DoubleClick", "LV_DoubleClick")

; ┌─────────────────────────────────────────────────────────────────────────────┐
; │  Gather a list of file names from a folder and put them into the ListView:  │
; └─────────────────────────────────────────────────────────────────────────────┘
PopulateMainListView()

; ┌─────────────────────────────────────────────┐
; │  Show an input box and retrieve the result  │
; └─────────────────────────────────────────────┘
InputBtn := MyGui.Add("Button", "s8 xc+10 y+10", "Input Test")
InputBtn.OnEvent("Click", "InputTest")
DirSelectBtn := MyGui.Add("Button", "s8 x+5 yp", "DirSelect")
DirSelectBtn.OnEvent("Click", "DirSelectForLV")

; GetContentBtn := MyGui.Add("Button", "xc+100 yp", "Get LV Content")

;LV.ModifyCol()  ; Auto-size each column to fit its contents.
LV.ModifyCol(2, "Integer")  ; For sorting purposes, indicate that column 2 is an integer.

; ┌─────────────────────┐
; │  Add a radio group  │
; └─────────────────────┘

RadioText := MyGui.Add("Text", "w200 h20 xc+10", "Radio group tests")
RadioText.SetFont("cBlue s10")
RadioOne := MyGui.Add("Radio", "vMyRadioGroup", "Change header font (alternate)")
RadioOne.OnEvent("Click", "ChangeFont")
RadioTwo := MyGui.Add("Radio", "vMyRadioGroup", "Restore header font (alternate)")
RadioTwo.OnEvent("Click", "ChangeFontBack")
RadioThree := MyGui.Add("Radio", "vMyRadioGroup", "Please click me")
RadioThree.OnEvent("Click", "RadioThreeClicked")

; ┌──────────────────┐
; │  Add checkboxes  │
; └──────────────────┘

CheckBoxText := MyGui.Add("Text", "w200 h20", "Checkbox test")
CheckBoxText.SetFont("cBlue s10")
CheckBoxOne := MyGui.Add("CheckBox", "w200 xc+10 yp+20", "If this text is long, it will wrap automatically")
CheckBoxOne.OnEvent("Click", "CheckBoxOneClicked")

; ┌────────────────────────────────┐
; │  Notify User about Popup Menu  │
; └────────────────────────────────┘

; Give this header an explicit height: SetFont() enlarges the text to s14 after the control was already
; auto-sized at the GUI's default (s8) font, so without a reserved height the next control would overlap it.
Menu_Label := MyGui.Add("Text", "w400 h28 xc+10 y+10","Press Win-Z to see popup menu")
Menu_Label.SetFont("cBlue s14")

checkBtn := MyGui.Add("Button", "xc+10 y+3", "ControlSetChecked")
checkBtn.OnEvent("Click", "SetChecked")

menuIndexBtn := MyGui.Add("Button", "x+5 yp", "Select menu by index")
menuIndexBtn.OnEvent("Click", "SelectMenuByIndex")

menuStringBtn := MyGui.Add("Button", "xc+10 y+3", "Select menu by string")
menuStringBtn.OnEvent("Click", "SelectByString")

#if WINDOWS
sysMenuMinimizeBtn := MyGui.Add("Button", "x+5 yp", "Minimize by system menu")
sysMenuMinimizeBtn.OnEvent("Click", "MinimizeBySystemMenu")
#endif

MyGui.UseGroup()
Tab.UseTab("Lists, Menus & Styles")
gb2_TabOne := MyGui.Add("GroupBox", "xc+350 yp w325", "Tab One - Group Two") ;
MyGui.UseGroup(gb2_TabOne)
; ┌───────────────────────────────┐
; │  Tab One, Group Two controls  │
; └───────────────────────────────┘

g2Label1 := MyGui.Add("Text", "w200 cBlue S10", "Click buttons to set and reset style")
g2Label2 := MyGui.Add("Text", "xc+10", "Keep an eye on the title bar!")

g2Btn1 := MyGui.Add("Button", "xc+10 y+10", "Set")
g2Btn1.SetFont("s10 cBlue")
g2Btn2 := MyGui.Add("Button", "xc+100 yp", "Reset")
g2Btn2.SetFont("s10 cBlue")

g2Btn1.OnEvent("Click", "Set_Style")
g2Btn2.OnEvent("Click", "Reset_Style")

g2Label3 := MyGui.Add("Text", "xc+10 w200 cBlue S10", "Click buttons to alter Edit style")
g2Label4 := MyGui.Add("Text", "xc+10", "Uppercase - restrict or reset")

MyEdit2 := MyGui.Add("Edit", "xc+10 w300 h55")
HwndMyEdit := MyEdit2.Hwnd

g2Btn3 := MyGui.Add("Button", "xc+10 y+10", "Uppercase")
g2Btn3.SetFont("s8 cBlue")
g2Btn4 := MyGui.Add("Button", "xc+100 yp", "Unrestrict")
g2Btn4.SetFont("s8 cBlue")

g2Btn3.OnEvent("Click", "Set_Edit_Style")
g2Btn4.OnEvent("Click", "Reset_Edit_Style")


; (IniRead/IniWrite are exercised by the NUnit unit tests; this manual harness does not duplicate them.)

MyGui.UseGroup()


;;;;;;;;;
; ┌──────────────────────┐
; │  Second Tab section  │
; └──────────────────────┘
Tab.UseTab("Edits & Messages")

; ┌─────────────────────────────┐
; │  Add group boxes - 8/23/22  │
; └─────────────────────────────┘
gb1_TabTwo := MyGui.Add("GroupBox", "xc+10 yc+10 w325", "Tab Two - Group One") ;
MyGui.UseGroup(gb1_TabTwo)

; ┌────────┐
; │  Edit  │
; └────────┘
SecondEdit := MyGui.Add("Edit", "xc+10 yc+20 w300 h110")
SecondEditText := MyGui.Add("Text", "cBlue s10 w200", "ControlSetText Test")
HwndSecondEdit := SecondEdit.Hwnd
EditBtn1 := MyGui.Add("Button", "xp y+10", "Text -> Edit")
EditBtn1.OnEvent("Click", "SendTextToEdit")
EditBtn2 := MyGui.Add("Button", "x+5 yp", "Clear Edit")
EditBtn2.OnEvent("Click", "ClearEdit")
EditHwndBtn := MyGui.Add("Button", "x+5 yp", "Show Edit Hwnd")
EditHwndBtn.OnEvent("Click", "ShowEditHwnd")

; ┌────────────┐
; │  RichEdit  │
; └────────────┘
SecondRichEdit := MyGui.Add("RichEdit", "xc+10 w250 h90", "Try pasting rich text and/or images here!")
SecondRichEditText := MyGui.Add("Text", "cBlue s10 w200", "ControlSetText Test (RichEdit)")
RichEditBtn1 := MyGui.Add("Button", "xc+10 y+10", "Send Text to RichEdit")
RichEditBtn1.OnEvent("Click", "SendTextToRichEdit")
RichEditBtn2 := MyGui.Add("Button", "x+5 yp", "Send Rtf to RichEdit")
RichEditBtn2.OnEvent("Click", "SendRtfToRichEdit")
RichEditBtn3 := MyGui.Add("Button", "xc+10 y+5", "Clear RichEdit")
RichEditBtn3.OnEvent("Click", "ClearRichEdit")
LinesBtn := MyGui.Add("Button", "x+5 yp", "EditGetLineCount")
LinesBtn.OnEvent("Click", "GetLineCount")

; ┌────────────┐
; │  TreeView  │
; └────────────┘
TreeViewText := MyGui.Add("Text", "xc+10 cBlue s10 w200", "TreeView Test")
TV := MyGui.Add("TreeView", "xp w200 y+5 -ReadOnly") ; Need to work on -ReadOnly
TV.OnEvent("ItemEdit", "MyTreeView_Edit")
P1 := TV.Add("First parent")
P1C1 := TV.Add("Parent 1's first child", P1)  ; Specify P1 to be this item's parent.
P2 := TV.Add("Second parent")
P2C1 := TV.Add("Parent 2's first child", P2)
P2C2 := TV.Add("Parent 2's second child", P2)
P2C2C1 := TV.Add("Child 2's first child", P2C2)

; ┌──────────────────────────┐
; │  Text to show Mouse Pos  │
; └──────────────────────────┘
MousePosText := MyGui.Add("Text", "xc+10 y+10 cBlue s10 w200", "Uses SetTimer to show mouse position")
; The size/weight are set directly in the Add options (s16 bold) so the control is created at its final font
; and autosizes to the real 16pt line height on every platform; the enclosing groupbox (which autosizes to
; its children) then reserves enough room. Enlarging the font afterwards via SetFont would be too late - the
; groupbox captures the control's smaller default-font height before the change takes effect.
CoordText := MyGui.Add("Text", "xc+10 y+10 cLime s16 bold", "")
SetTimer("UpdateOSD", 200)
UpdateOSD()  ; Make the first update immediate rather than waiting for the timer.

; (ImageSearch runs on the Image tab against the colour swatch in pixelGroup — see imgSearchBtn / ImgSrch.)
; ┌────────────────────────┐
; │  End of Tab 2 Group 1  │
; └────────────────────────┘
MyGui.UseGroup()
Tab.UseTab("Edits & Messages")
gb2_TabTwo := MyGui.Add("GroupBox", "xc+350 yc+10 w400", "Tab Two - Group Two")
MyGui.UseGroup(gb2_TabTwo)

; ┌─────────┐
; │  Edits  │
; └─────────┘
t2g2t1 := MyGui.Add("Text", "xc+10 yc+20 w200 cBlue", "Password entry")
t2g2t1.SetFont("s10")
e1 := MyGui.Add("Edit", "w200 xp y+10 +0x20")
e1.SetCue("Password cue text", 1) ; Does not disappear when control has keyboard focus
t2g2t2 := MyGui.Add("Text", "xp y+10 w250 cBlue s10", "Alternate password entry (*)")
t2g2t2.SetFont("s10")
e2 := MyGui.Add("Edit", "w200 xp y+10 Password*")

#if WINDOWS
	t2g2t3 := MyGui.Add("Text", "xp y+10 w250 cBlue", "Uppercase - ControlSetStyle")
#else
	t2g2t3 := MyGui.Add("Text", "xp y+10 w250 cBlue", "Uppercase - Opt(`"+Uppercase`")")
#endif

t2g2t3.SetFont("s10")
e3 := MyGui.Add("Edit", "w200 xp y+10 h50", "Edit 3")
MyGui.UseGroup(gb2_TabTwo)

#if WINDOWS
	ControlSetStyle("+0x8", e3)
#else
	e3.Opt("+Uppercase")
#endif

#if WINDOWS
	t2g2t4 := MyGui.Add("Text", "xp y+10 w250 cBlue", "Uppercase - +0x8")
#else
	t2g2t4 := MyGui.Add("Text", "xp y+10 w250 cBlue", "Uppercase - Constructor")
#endif

t2g2t4.SetFont("s10")

#if WINDOWS
	e4 := MyGui.Add("Edit", "w200 xp y+10 h50 +0x8", "Edit 4")
#else
	isUpper := true
	e4 := MyGui.Add("Edit", "w200 xp y+10 h50 Uppercase", "Edit 4")
#endif

e3Btn := MyGui.Add("Button", "xp y+10", "Toggle ControlSetStyle Edit")
e3Btn.OnEvent("Click", "ShowE3Hwnd")

numericText := MyGui.Add("Text", "xc+10 y+10 Autosize", "The text box below should be numeric only")
numericText.SetFont("s10 cBlue")
numericEdit := MyGui.Add("Edit", "w200 xp y+10 number")

setNumericBtn := MyGui.Add("Button", "x+10 yp", "Num")
setNumericBtn.SetFont("s8 cBlue")
setNumericBtn.OnEvent("Click", "SetNumeric")

resetNumericBtn := MyGui.Add("Button", "x+10 yp", "Unr")
resetNumericBtn.SetFont("s8 cBlue")
resetNumericBtn.OnEvent("Click", "ClearNumeric")

SetNumeric(*)
{
	numericEdit.Opt("+Number")
}

ClearNumeric(*)
{
	numericEdit.Opt("-Number")
}

ShowE3Hwnd(*)
{
#if WINDOWS
	ControlSetStyle("^0x8", e3)
#else
	global isUpper
	if (isUpper)
		e3.Opt("")
	else
		e3.Opt("+Uppercase")
	isUpper := !isUpper
#endif
	ControlFocus(e3.Hwnd)
}

; (The Move-GUI / Change-Title / Run-Notepad window tests are on the Windows tab, since they manipulate whole windows.)

MyGui.UseGroup()
Tab.UseTab("Edits & Messages")

; (The Picture display/destroy buttons are on the Image tab — see imgDisplayBtn / imgDestroyBtn.)

;;;;;;;;;;
; ┌─────────────────────┐
; │  Third Tab section  │
; └─────────────────────┘

Tab.UseTab("Pickers & Sliders")
; ┌──────────────────┐
; │  Add a groupbox  │
; └──────────────────┘

gb1_TabThree := MyGui.Add("GroupBox", "xc+10 yc+10 w325", "Tab Three - Group One")
MyGui.UseGroup(gb1_TabThree)

;Placeholder ThirdText1
ThirdText1 := MyGui.Add("Text", "xc+10 yc+20 cBlue s10", "ListBox Test")
; ┌────────────────┐
; │  ListBox test  │
; └────────────────┘
MyListBox := MyGui.Add("ListBox", "xc+10 r4 w110", ["Red","Green","Blue","Black","White"])
MyListBox.OnEvent("Change", "ListBoxClicked")

MyLbBtn1 := MyGui.Add("Button", "x+10 yp", "Delete White")
MyLbBtn1.OnEvent("Click", "DeleteWhite")
MyLbBtn2 := MyGui.Add("Button", "x+10 yp", "Add White")
MyLbBtn2.OnEvent("Click", "AddWhite")

DeleteWhite(*) {

	Try
	{
		WhiteIndex := ControlFindItem("White", MyListBox)
	}
	Catch as e  ; Handles the first error thrown by the block above.
	{
		MsgBox("An error was thrown!`nSpecifically: " e.Message, "ERROR!")
		Return
	}

	ControlDeleteItem(WhiteIndex, MyListBox)
}

AddWhite(*) {
	ControlAddItem("White", MyListBox)
}

; ┌────────────────┐
; │  Multi-select  │
; └────────────────┘
; No Y coordinate: this positions the label beneath all controls in the group (i.e. below the listbox
; above it). Using y+ here would measure from the buttons beside the listbox, which sit at its top, so
; the gap wouldn't account for the listbox height (which differs across platforms/fonts).
ThirdText2 := MyGui.Add("Text", "xc+10 cBlue s10", "ListBox Test (Multi-Select)")
MyMultiLB := MyGui.Add("ListBox", "+Multi r3 w110 xc+10 y+10", ["Reactionary Red","Garish Green","Beastly Blue","Banal Black","Washed-out White"])
MyMultiLB.OnEvent("Change", "MultiLBClicked")

; ┌─────────────┐
; │  Drop-Down  │
; └─────────────┘
ThirdText3 := MyGui.Add("Text", "xc+10 y+10 cBlue s10", "Drop-down List with 5 rows")
MyDDL := MyGui.Add("DropDownList", "xc+10 y+10 r3", ["Orange","Purple","Fuchsia","Lime","Aqua"])
MyDDL.OnEvent("Change", "DDLClicked")

; ┌─────────────┐
; │  Combo Box  │
; └─────────────┘
ThirdText4 := MyGui.Add("Text", "xc+10 cBlue s10", "ComboBox with 3 rows")
MyCB := MyGui.Add("ComboBox", "xc+10 y+10 r3", ["Orange","Purple","Fuchsia"])
CB_Button := MyGui.Add("Button", "h25 w80 xc+10 y+10", "CB Selection")
CB_AddBtn := MyGui.Add("Button", "h25 w80 xc+90 yp", "Add Yellow")
CB_Button.OnEvent("Click", "CB_ButtonClicked")
CB_AddBtn.OnEvent("Click", "AddYellow")
CB_DeleteBtn := MyGui.Add("Button", "h25 w80 xc+170 yp", "Del Yellow")
CB_DeleteBtn.OnEvent("Click", "DeleteYellow")

AddYellow(*) {
	ControlAddItem("Yellow", MyCB)
}

DeleteYellow(*) {

	Try
	{
		YellowIndex := ControlFindItem("Yellow", MyCB)
	}
	Catch as e  ; Handles the first error thrown by the block above.
	{
		MsgBox("An error was thrown!`nSpecifically: " e.Message, "ERROR!")
		Return
	}

	ControlDeleteItem(YellowIndex, MyCb)
}

; ┌──────────┐
; │  Slider  │
; └──────────┘

ThirdText5 := MyGui.Add("Text", "xc+10 cBlue s10", "Moving slider shows position below")
MySlider := MyGui.Add("Slider", "xc+10 y+10 +AltSubmit TickInterval10 Page10", 100)
MySlider.OnEvent("Change", "SliderPos")
MySliderPos := MyGui.Add("Text", "xc+10 y+5","")

; ┌───────────────────┐
; │  Slider Callback  │
; └───────────────────┘
SliderPos(*) {
	ControlSetText("Slider value is " MySlider.Value, MySliderPos.Hwnd)
}

; ┌────────────────┐
; │  Progress Bar  │
; └────────────────┘
ThirdText6 := MyGui.Add("Text", "xc+10 cBlue s10", "Progress bar - click buttons to move")
MyProgress := MyGui.Add("Progress", "xc+10 y+10 cRed BackgroundGreen Smooth", 50)
MyProgress.GetPos(&px, &py, &pw, &ph)
MyVertProgress := MyGui.Add("Progress", "cRed BackgroundGreen Smooth x+70 yp-" . (pw - ph) . " Vertical w" . ph . " h" . pw, 50) ; Swap width and height.

Pbtn1 := MyGui.Add("Button", "s8 xc+10 y+5", "Lower")
Pbtn2 := MyGui.Add("Button", "s8 xc+100 yp", "Higher")
ProgressStatusText := MyGui.Add("Text", "x+5 yp cBlue s10 Autosize", "Value: ")
Pbtn1.OnEvent("Click", "Pbtn1Clicked")
Pbtn2.OnEvent("Click", "Pbtn2Clicked")

; ┌─────────────┐
; │  Date Time  │
; └─────────────┘
; y+ is measured from the previous control (the short "Value:" status text), but that text shares a row
; with the taller Lower/Higher buttons, so the offset must clear the buttons' bottom edge rather than the text.
; (DateTime + MonthCal are in the "Tab Three - Dates" group on the right of this tab — see gb3_TabThree.)

; ┌───────────────────────────┐
; │  End Tab Three Group One  │
; └───────────────────────────┘

MyGui.UseGroup()
Tab.UseTab("Pickers & Sliders")
gb2_TabThree := MyGui.Add("GroupBox", "xc+350 yc+10 w325", "Tab Three - Group Two")
MyGui.UseGroup(gb2_TabThree)
; ┌────────────────┐
; │  Sliding text  │
; └────────────────┘

InfoText3 := MyGui.Add("Text", "xc+10 yc+20 w200", "Sliding text. Move Slider.")
InfoText3.SetFont("cBlue s8")
MyText := MyGui.Add("Text", "xc+10 y+10 w300 h30")
MyText.SetFont("cTeal Consolas Bold")
HwndMyText := MyText.Hwnd

MySlider2 := MyGui.Add("Slider", "Range0-80 +AltSubmit TickInterval10 Page10 ToolTip", 10)
MySlider2.Value := 10
mybtn := MyGui.Add("Button", "w100 s8 cBlue", "Sliding Test")
mybtn.OnEvent("Click", "STest")
FakeSep := MyGui.Add("Text", "xc+10 y+10", "__________________________________________________")
FakeSep.SetFont("cTeal Bold")

STest(*) {
	Loop(MySlider2.Value) {
		padding := A_Index
		s := FormatCs("| {1,-" padding "} |`r`n| {2," padding "} |`r`n", "Left  ", "Right")
		ControlSetText(s, HwndMyText)
		Sleep(5) ; Need time to update Text

	}
	Loop(MySlider2.Value) {
		padding := MySlider2.Value-A_Index
		s := FormatCs("| {1,-" padding "} |`r`n| {2," padding "} |`r`n", "Left  ", "Right")
		ControlSetText(s, HwndMyText)
		Sleep(5)
	}
}

MyLinkText := MyGui.Add("Text", "xc+10 y+5", "Link test")
MyLinkText.SetFont("cBlue s8")
MyLink := MyGui.Add("Link", "xc+10 y+5", 'Click this <a href="https://www.autohotkey.com">link to AHK page</a>')

MyHkInfoText := MyGui.Add("Text", "xc+10 y+5 w200", "Define Hotkey test`nFocus Edit and click hotkey(s)")
MyHkInfoText.SetFont("cBlue s8")
MyHotkey := MyGui.Add("Hotkey", "xc+10 y+5")
MyHotkey.OnEvent("Change", "UpdateHK")
MyHkText := MyGui.Add("Text", "xc+10 y+5 w200" , MyHotkey.Value)
;MyHkText2 := MyGui.Add("Text", "xc+10 y+5 w200 cRed", "NOTE: Combos w/Win not working.")

UpdateHK(*) {
	ControlSetText(MyHotkey.Value, MyHkText)
}

FakeSep2 := MyGui.Add("Text", "xc+10 y+5", "__________________________________________________")
FakeSep2.SetFont("cTeal Bold")

MyGui.Add("Text", "xc+10 y+5", "UpDown: Range 1-10, `ninc 1 (mouse 8), def 5.")
nud := MyGui.Add("UpDown", "xc+10 y+5 h25 vMyNud Range1-10", 5)
nud.OnEvent("Change", "NudChange")

MyGui.Add("Text", "xc+10 y+5", "UpDown: Range -2000-2000, def 0,`ninc 100 (mouse 800), no separator.")
nud2 := MyGui.Add("UpDown", "xc+10 y+5 h20 vMyNud2 Range-2000-2000 Increment100 0x80", 0)
nud2.OnEvent("Change", "NudChange")

MyGui.Add("Text", "xc+10 y+5", "UpDown: Range -1000-1000, def 0,`ninc 10 (mouse 80), hex.")
nud3 := MyGui.Add("UpDown", "xc+10 y+5 h20 vMyNud3 Range-1000-1000 Increment10 hex 0x80", 0)
nud3.OnEvent("Change", "NudChange")

nudTxt := MyGui.Add("Text", "xc+10 y+5 w200", "Nud values:")

MyGui.UseGroup()

NudChange(*)
{
	nudTxt.Value := "Nud values: " . nud.Value . ", " . nud2.Value . ", " . nud3.Value
}

	; ┌────────────────────────┐
	; │  Groupbox Tab Section  │
	; └────────────────────────┘

; ── Tab Three - Dates: DateTime + MonthCal in their own right-hand group ──
Tab.UseTab("Pickers & Sliders")
gb3_TabThree := MyGui.Add("GroupBox", "xc+690 yc+10 w330", "Tab Three - Dates")
MyGui.UseGroup(gb3_TabThree)
ThirdText7 := MyGui.Add("Text", "xc+16 yc+24 cBlue s10", "DateTime Test")
MyDateTime := MyGui.Add("DateTime", "s8 xc+16 y+8 w200", "LongDate")
ThirdText8 := MyGui.Add("Text", "xc+16 y+12 cBlue s10", "MonthCal Test")
MyMonthCal := MyGui.Add("MonthCal", "xc+16 y+5")
MC_Btn := MyGui.Add("Button", "s8 xc+16 y+8", "Change Cal Colors (not implemented)")
MC_Btn.OnEvent("Click", "MC_Colors")
MyGui.UseGroup()

	; Image-copy (Paste Pic / Paste from File) and send-to-control-edit tests, grouped on the ControlZoo tab.
	Tab.UseTab("ControlZoo")
	gb3_CZ := MyGui.Add("GroupBox", "xc+880 yc+10 w350", "Image Copy & Send-to-Control")
	MyGui.UseGroup(gb3_CZ)
	CpText := MyGui.Add("Text", "xc+16 yc+24 w310", "Image copy — Paste Pic / Paste from file share this edit:")
	CpText.SetFont("s8 cBlue")
	MyRE := MyGui.Add("RichEdit", "xc+16 y+8 w310 h120")
	MySecondPic := LoadPicture(A_WorkingDir . A_DirSeparator . "Robin.png")
	CopyImageToClipboard("HBITMAP:" MySecondPic)
	ShowBtn := MyGui.Add("Button", "xc+16 y+8 w110", "Paste Pic")
	ShowBtn.OnEvent("Click", "PastePic")
	ShowBtn2 := MyGui.Add("Button", "x+10 yp w130", "Paste from file")
	ShowBtn2.OnEvent("Click", "CopyPicFromFile")

PastePic(*) {
	ControlFocus(MyRE)
	Send("^v")
}

; Both image-copy buttons share the single MyRE edit above.
CopyPicFromFile(*) {
	SelectedFile := FileSelect("3", A_AppData . A_DirSeparator . "Pictures")

	if (SelectedFile != "")
	{
		CopyImageToClipboard(SelectedFile)
		Sleep(100)
		ControlFocus(MyRE)
		Send("^v")
	}
}
	gb3Label := MyGui.Add("Text", "xc+16 y+14 w310", "Send text to this edit (buttons below):")
	gb3Label.SetFont("s8 cBlue")
	gb3Edit := MyGui.Add("Edit", "xc+16 y+8 w310 h130")
	gb3Hwnd := gb3Edit.Hwnd
	gb3Edit.OnEvent("Focus", "StartEditTooltip")
	gb3Edit.OnEvent("LoseFocus", "StopToolTip")
	gb4Btn1 := MyGui.Add("Button", "xc+16 y+8 w110 cLime", "Send to GB3")
	gb4Btn1.OnEvent("Click", "SendToGB3")
	gb4Btn2 := MyGui.Add("Button", "x+10 yp w110 cLime", "Clear GB3")
	gb4Btn2.OnEvent("Click", "ClearGB3")
	MyGui.UseGroup()

; ┌────────────────┐
; │  MENU SECTION  │
; └────────────────┘

MyMenu := Menu()
MyMenu.Add("Item 1", "MenuHandler")
MyMenu.Add("Item 2", "MenuHandler")
MyMenu.Add()  ; Add a separator line.

; Create another menu destined to become a submenu of the above menu.
Submenu1 := Menu()
Submenu1.Add("Item A", "MenuHandler")
Submenu1.Add("Item B", "MenuHandler")

; Create a submenu in the first menu (a right-arrow indicator). When the user selects it, the second menu is displayed.
MyMenu.Add("My Submenu", Submenu1)

MyMenu.Add()  ; Add a separator line below the submenu.
MyMenu.Add("Item 3", "MenuHandler")  ; Add another menu item beneath the submenu.

MenuHandler(Item, *) {
	MsgBox("You selected " Item, "ITEM SELECTED")

	if (Item == "S&cript Icon")
		TraySetIcon(A_KeysharpCorePath, "Keysharp.ico")
	else if (Item == "S&uspend Icon")
		TraySetIcon(A_KeysharpCorePath, "Keysharp_s.ico")
	else if (Item == "&Pause Icon")
		TraySetIcon(A_KeysharpCorePath, "Keysharp_p.ico")
	else if (Item == "&System")
		TraySetIcon("Shell32.dll", 174)
	else
		TraySetIcon(A_KeysharpCorePath, "Keysharp.ico")
}

; The three radio items behave as one group, so picking any of them should move the bullet off whichever
; item held it. MenuObj is the menu the item was selected from, which saves naming PresentationMenu here.
PickPresentationRadio(Item, Pos, MenuObj) {
	loop 3
		MenuObj.UnCheck("Radio choice &" A_Index)

	MenuObj.Check(Item)
}

TogglePresentationCheck(Item, Pos, MenuObj) {
	MenuObj.ToggleCheck(Item)
}

#z::
{
	KeyWait "LWin" ; This is necessary in X11+inputd, when a grab on Win can block the context menu from showing
	MyMenu.Show()  ; i.e. press the Win-Z hotkey to show the menu.
}
;#z::Run("Notepad.exe")

; ┌──────────────────┐
; │  ControlZoo Tab  │
; └──────────────────┘

MyGui.UseGroup()
Tab.UseTab("ControlZoo")
gb1_CZ := MyGui.Add("GroupBox", "xc+10 yc+10 w460", "ControlZoo - Group One")
MyGui.UseGroup(gb1_CZ)
CZ_Text1 := MyGui.Add("Text", "xc+10 yc+20", "Control Functions testing")
CZ_Text1.SetFont("s10 CBlue")
CZ_Text2 := MyGui.Add("Text", "xc+10 y+10 w300 h30 Wrap Border", "For the controls on this tab, we'll add, delete, click, focus and perform other control functions.")
CZ_Text2.SetFont("CTeal")

CZ_Text2a := MyGui.Add("Text", "xc+10 y+5", "ListBox control testing")
CZ_Text2a.SetFont("s8 CBlue")

CZ_ListBox := MyGui.Add("ListBox", "xc+10 h120 w160 Section", ["Red","Green","Blue","Black","White", "Maroon"
	, "Purple", "Color de gos com fuig", "Weiß", "Amarillo", "красный"
	, "朱红"])

CZ_Text3 := MyGui.Add("Text", "xc+10 y+5", "Edit control testing")
CZ_Text3.SetFont("s8 CBlue")

CZ_Edit1 := MyGui.Add("Edit", "xc+10 y+5 w160 h60")
;CZ_Edit1.SetCue("Multi-line edit control cue text")

CZ_SeparatorText1 := MyGui.Add("Text", "xc+10 y+8 w160", "ListView content tests")
CZ_SeparatorText1.SetFont("s8 CBlue")

LV2 := MyGui.Add("ListView", "r4 w160 xc+10 y+5", ["Name","KB"])

Loop Files A_MyDocuments . A_DirSeparator . "*.*"
	LV2.Add(, A_LoopFileName, A_LoopFileSizeKB)

LV2_Btn1 := MyGui.Add("Button", "xc+10 y+5 w76 h24" ,"Selected")
LV2_Btn1.OnEvent("Click", "LV_Selected")

LV2_Btn2 := MyGui.Add("Button", "x+4 yp w76 h24" ,"Focused")
LV2_Btn2.OnEvent("Click", "LV_Focused")

LV2_Btn3 := MyGui.Add("Button", "xc+10 y+4 w76 h24", "Column 1")
LV2_Btn3.OnEvent("Click", "LV_Col1")

LV2_Btn4 := MyGui.Add("Button", "x+4 yp w76 h24", "Count")
LV2_Btn4.OnEvent("Click", "LV_Count")

LV2_Btn5 := MyGui.Add("Button", "xc+10 y+4 w156 h24", "Count Selected")
LV2_Btn5.OnEvent("Click", "LV_CountSelected")

LV2_Btn6 := MyGui.Add("Button", "xc+10 y+4 w156 h24", "Row Focused")
LV2_Btn6.OnEvent("Click", "LV_CountFocused")

LV2_Btn7 := MyGui.Add("Button", "xc+10 y+4 w156 h24", "Count Columns")
LV2_Btn7.OnEvent("Click", "LV_CountCol")


	; ┌─────────────────────────────────────────────┐
	; │  ControlZoo - end of Group One, Column One  │
	; └─────────────────────────────────────────────┘


CZ_LbBtn1 := MyGui.Add("Button", "xs+170 ys w120 h25 Section", "Add Fuchsia")
CZ_LbBtn1.OnEvent("Click", "AddFuchsia")
CZ_LbBtn2 := MyGui.Add("Button", "x+8 yp w120 h25", "Delete Fuchsia")
CZ_LbBtn2.OnEvent("Click", "DeleteFuchsia")
CZ_LbBtn2.OnEvent("Focus", "FuchsiaDeleteTrayTip")
CZ_LbBtn3 := MyGui.Add("Button", "xs y+4 w120 h25", "Purple (Index)")
CZ_LbBtn3.OnEvent("Click", "ChooseIndex")
CZ_LbBtn4 := MyGui.Add("Button", "x+8 yp w120 h25", "красный (String)")
CZ_LbBtn4.OnEvent("Click", "ChooseString")
CZ_LbBtn5 := MyGui.Add("Button", "xs y+4 w120 h25", "ControlGetChoice")
CZ_LbBtn5.OnEvent("Click", "GetChoice")
CZ_LbBtn19 := MyGui.Add("Button", "x+8 yp w120 h25", "ControlGetIndex")
CZ_LbBtn19.OnEvent("Click", "GetIndex")
CZ_LbBtn6 := MyGui.Add("Button", "xs y+4 w120 h25", "ControlGetClassNN")
CZ_LbBtn6.OnEvent("Click", "GetClassNN")

CZ_LbBtn7 := MyGui.Add("Button", "x+8 yp w120 h25", "ControlGetEnabled")
CZ_LbBtn7.OnEvent("Click", "GetEnabled")
CZ_LbBtn20 := MyGui.Add("Button", "xs y+4 w120 h25", "ControlSetEnabled")
CZ_LbBtn20.OnEvent("Click", "SetEnabled")
CZ_LbBtn8 := MyGui.Add("Button", "x+8 yp w120 h25", "Disabled!")
CZ_LbBtn8.Enabled := False

CZ_LbBtn9 := MyGui.Add("Button", "xs y+4 w120 h25", "ControlGetHwnd")
CZ_LbBtn9.OnEvent("Click", "GetHwnd")

CZ_LbBtn10 := MyGui.Add("Button", "x+8 yp w120 h25", "ControlGetText")
CZ_LbBtn10.OnEvent("Click", "GetText")

CZ_LbBtn11 := MyGui.Add("Button", "xs y+4 w120 h25", "ControlHide")
CZ_LbBtn11.OnEvent("Click", "HideButton")

CZ_LbBtn12 := MyGui.Add("Button", "x+8 yp w120 h25", "ControlShow")
CZ_LbBtn12.OnEvent("Click", "ShowButton")

CZ_LbBtn13 := MyGui.Add("Button", "xs y+4 w120 h25", "Visible?")
CZ_LbBtn13.OnEvent("Click", "IsItHidden")

CZ_LbBtn21 := MyGui.Add("Button", "x+8 yp w120 h25", "Get Focus")
CZ_LbBtn21.OnEvent("Click", "GetFocusCtrl")

#if WINDOWS
CZ_LbBtn22 := MyGui.Add("Button", "xs y+4 w120 h25", "ControlSetExStyle")
CZ_LbBtn22.OnEvent("Click", "ToggleEditExStyle")

CZ_LbBtn14 := MyGui.Add("Button", "x+8 yp w120 h25", "Edit Column #")
#else
; ControlSetExStyle is Windows-only and is omitted above, so start a new row here
; instead of placing this button to the right of "Get Focus" (which would overflow the group).
CZ_LbBtn14 := MyGui.Add("Button", "xs y+4 w120 h25", "Edit Column #")
#endif
CZ_LbBtn14.OnEvent("Click", "GetCol")

CZ_LbBtn15 := MyGui.Add("Button", "xs y+4 w120 h25", "Edit Line #")
CZ_LbBtn15.OnEvent("Click", "GetLine")

CZ_LbBtn16 := MyGui.Add("Button", "x+8 yp w120 h25", "Edit Line Text")
CZ_LbBtn16.OnEvent("Click", "GetLineText")

CZ_LbBtn17 := MyGui.Add("Button", "xs y+4 w120 h25", "Selected text")
CZ_LbBtn17.OnEvent("Click", "GetSelectedText")

CZ_LbBtn18 := MyGui.Add("Button", "x+8 yp w120 h25", "Edit Paste")
CZ_LbBtn18.OnEvent("Click", "EditPaster")

#if WINDOWS
customText := MyGui.Add("Text", "xc+10", "Custom controls:")
customText.SetFont("s8 CBlue")

IP := MyGui.Add("Custom", "ClassSysIPAddress32 r1 w150")
IP.OnCommand(0x300, IP_EditChange)
IP.OnNotify(-860, IP_FieldChange)
IPText := MyGui.Add("Text", "wp")
IPField := MyGui.Add("Text", "wp y+m")

IPCtrlSetAddress(IP, SysGetIPAddresses()[1])

IP_EditChange(*)
{
	IPText.Text := "New text: " IP.Text
}

IP_FieldChange(thisCtrl, NMIPAddress)
{
	; Extract info from the NMIPAddress structure.
	iField := NumGet(NMIPAddress, 3*A_PtrSize + 0, "int")
	iValue := NumGet(NMIPAddress, 3*A_PtrSize + 4, "int")

	if (iValue >= 0)
		IPField.Text := "Field #" iField " modified: " iValue
	else
		IPField.Text := "Field #" iField " left empty"
}

IPCtrlSetAddress(GuiCtrl, IPAddress)
{
	static WM_USER := 0x0400
	static IPM_SETADDRESS := WM_USER + 101

	; Pack the IP address into a 32-bit word for use with SendMessage.
	IPAddrWord := 0
	Loop Parse IPAddress, "."
		IPAddrWord := (IPAddrWord * 256) + A_LoopField

	SendMessage(IPM_SETADDRESS, 0, IPAddrWord, GuiCtrl)
}

IPCtrlGetAddress(GuiCtrl)
{
	static WM_USER := 0x0400
	static IPM_GETADDRESS := WM_USER + 102

	AddrWord := Buffer(4)
	SendMessage(IPM_GETADDRESS, 0, AddrWord, GuiCtrl)
	IPPart := []

	Loop 4
		IPPart.Push(NumGet(AddrWord, 4 - A_Index, "UChar"))

	return IPPart[1] "." IPPart[2] "." IPPart[3] "." IPPart[4]
}

#endif

MyGui.UseGroup()
Tab.UseTab("ControlZoo")
gb2_CZ := MyGui.Add("GroupBox", "x+10 yc+10 w370", "ControlZoo - Group Two")
MyGui.UseGroup(gb2_CZ)

;Reserved4 := MyGui.Add("Text", "xc+10 yc+20 w325", "Reserved for Future Testing")
;Reserved4.SetFont("s12 CBlue")
gb2_CZ_Text1 := MyGui.Add("Text", "xc+10 yc+20 w325", "ComboBox Control Tests")
gb2_CZ_Text1.SetFont("s8 cBlue")

gb2_CZ_CB := MyGui.Add("ComboBox", "xc+10 y+10 r5 Limit", ["Orange","Purple","Fuchsia","Lime","Aqua"])
#if WINDOWS
gb2_CZ_CB.SetCue("ComboBox cue text")
#endif
gb2_CZ_Btn1 := MyGui.Add("Button", "xc+10 y+5 w80 h25", "Add White")
gb2_CZ_Btn1.OnEvent("Click", "AddWhite2")
gb2_CZ_Btn2 := MyGui.Add("Button", "xc+90 yp w80 h25", "Delete White")
gb2_CZ_Btn2.OnEvent("Click", "DeleteWhite2")
gb2_CZ_Btn3 := MyGui.Add("Button", "xc+170 yp w80 h25", "-> Purple")
gb2_CZ_Btn3.OnEvent("Click", "ChooseString_CB")

gb2_CZ_Btn4 := MyGui.Add("Button", "xc+10 y+5 w200 h25", "Click Win+R, show dropdown")
gb2_CZ_Btn4.OnEvent("Click", "Click_CB")

gb2_CZ_Btn5 := MyGui.Add("Button", "xc+10 y+5", "Show ListBox items")
gb2_CZ_Btn5.OnEvent("Click", "Click_LB_Items")

gb2_CZ_Btn6 := MyGui.Add("Button", "x+5 yp", "Show ComboBox items")
gb2_CZ_Btn6.OnEvent("Click", "Click_CB_Items")

gb2_CZ_Btn7 := MyGui.Add("Button", "xc+10 y+5", "Show ComboBox dropdown")
gb2_CZ_Btn7.OnEvent("Click", "Click_CB_Show_Dropdown")

gb2_CZ_Btn8 := MyGui.Add("Button", "x+5 yp", "Hide ComboBox dropdown")
gb2_CZ_Btn8.OnEvent("Click", "Click_CB_Hide_Dropdown")

gb2_CZ_Text2 := MyGui.Add("Text", "xc+10 y+10 w325", "Move mouse to color. Press Ctrl+Alt+9.")
gb2_CZ_Text2.SetFont("s8 cBlue")

MyColorLabel := MyGui.Add("Text", "xc+10 y+10 w200", "Empty text below:")
MyColorText := MyGui.Add("Text", "w200 xc+10 y+10", "")

; "Control Tests Redux" buttons laid out in two columns so none clip off the bottom of the group.
SecondGuiButton := MyGui.Add("Button", "xc+10 y+15 w160 h26 Section", "Control Tests Redux")
SecondGuiButton.OnEvent("Click", "SecondGUI")
FindEdit := MyGui.Add("Button", "xc+10 y+5 w160 h26", "Get Edit Hwnd")
FindEdit.OnEvent("Click", "FindSecondGuiEdit")

ThirdGuiButton := MyGui.Add("Button", "xc+10 y+5 w160 h26", "'Find By' Tests")
ThirdGuiButton.OnEvent("Click", "ThirdGUI")

MouseMoveButton := MyGui.Add("Button", "xc+10 y+5 w160 h26", "Mouse-moving tests")
MouseMoveButton.OnEvent("Click", "MoveTheMouse")

#if WINDOWS
AddMsgMonitorButton := MyGui.Add("Button", "xc+10 y+5 w160 h26", "Add msg mon (edit clicks)")
AddMsgMonitorButton.OnEvent("Click", "AddMsgMonitor")

RemoveMsgMonitorButton := MyGui.Add("Button", "xc+10 y+5 w160 h26", "Remove msg mon")
RemoveMsgMonitorButton.OnEvent("Click", "RemoveMsgMonitor")
#endif

; Second column (starts level with "Control Tests Redux")
MinimizeAllButton := MyGui.Add("Button", "xs+170 ys w160 h26", "Minimize all")
MinimizeAllButton.OnEvent("Click", "MinimizeAll")
UndoMinimizeAllButton := MyGui.Add("Button", "xs+170 y+5 w160 h26", "Undo minimize all")
UndoMinimizeAllButton.OnEvent("Click", "UndoMinimizeAll")
MaximizeAllButton := MyGui.Add("Button", "xs+170 y+5 w160 h26", "Maximize all")
MaximizeAllButton.OnEvent("Click", "MaximizeAll")
MoveAllButton := MyGui.Add("Button", "xs+170 y+5 w160 h26", "Move me")
MoveAllButton.OnEvent("Click", "MoveButton")
CandyProgressButton := MyGui.Add("Button", "xs+170 y+5 w160 h26", "Candy progress")
CandyProgressButton.OnEvent("Click", "CandyProgress")
TestTypesButton := MyGui.Add("Button", "xs+170 y+5 w160 h26", "Test types")
TestTypesButton.OnEvent("Click", "TestTypes")

MinimizeAll(*)
{
	WinMinimizeAll()
}

UndoMinimizeAll(*)
{
	WinMinimizeAllUndo()
}

MaximizeAll(*)
{
	WinMaximizeAll()
}

MoveButton(*)
{
	local x, y, w, h

	ControlGetPos(&x, &y, &w, &h, MoveAllButton.Hwnd, MyGui)
	x++
	y++
	ControlMove(x, y, w, h, MoveAllButton.Hwnd, MyGui)
}


candygui := Gui("-DPIScale +E0x02080000", "Candy Progress")
candygui.OnEvent("Close", "CloseCandy")
candygui.BackColor := "FFCC00"

CandyProgressBar := candygui.Add("Progress", "xc+15 yc+30 w436 h36 Smooth BackgroundSilver")

; These currently don't work on linux.
Icon1 := candygui.Add("Picture", "xc+15  yc+30 w18  h36 BackgroundTrans", "Icon1.png")
Icon2 := candygui.Add("Picture", "xc+33  yc+30 w400 h36 BackgroundTrans", "Icon2.png")
Icon3 := candygui.Add("Picture", "xc+433 yc+30 w18  h36 BackgroundTrans", "Icon3.png")

CandyText := candygui.Add("Text" ,"xc+15 yc+30 w436 h40 Center Middle BackgroundTrans")
CandyText.SetFont("cFFFFFF")

CandyTimerFunc := Func("CandyTimer")

CloseCandy(*) {
	global CandyTimerFunc
	SetTimer(CandyTimerFunc, 0)
}

CandyProgress(*)
{
	global

	if (!candygui.Visible)
	{
		candygui.Show("w485 h145")
		SetTimer(CandyTimerFunc, 100)
	}
	else
		candygui.Close()
}

candyvalue := 0

CandyTimer(*)
{
	global

	if (candyvalue >= 33) and (candyvalue <= 66) { ; These color changes don't seem to work.
		CandyProgressBar.Opt("cPurple")
	}
	else if (candyvalue >= 66) {
		CandyProgressBar.Opt("cAqua")
	}
	else {
		CandyProgressBar.Opt("cBlack")
	}

	CandyProgressBar.Value := candyvalue
	CandyText.Text := candyvalue . "%"
	candyvalue := candyvalue + 1
	if (candyvalue > 100)
	{
		Sleep(1000)
		candyvalue := 0
	}
}

TestTypes(*)
{
	global
	local s := "All of these should be true`n"

#if WINDOWS
	s .= "Odie is Gui.ActiveX: " . (activeXOdie is Gui.ActiveX) . "`n"
#endif
	s .= "Add Fuchsia is Gui.Button: " . (CZ_LbBtn1 is Gui.Button) . "`n"
	s .= "CheckBox test is Gui.CheckBox: " . (CheckBoxOne is Gui.CheckBox) . "`n"
	s .= "DateTime test is Gui.DateTime: " . (MyDateTime is Gui.DateTime) . "`n"
	s .= "Edit control testing is Gui.Edit: " . (CZ_Edit1 is Gui.Edit) . "`n"
	s .= "GroupBox 1 Tab 1 is Gui.GroupBox: " . (gb1_TabOne is Gui.GroupBox) . "`n"
	s .= "Define hotkey test is Gui.Hotkey: " . (MyHotkey is Gui.Hotkey) . "`n"
	s .= "Link test is Gui.Link: " . (MyLink is Gui.Link) . "`n" ; Link
	; List-derived controls.
	s .= "ComboBox control tests is Gui.ComboBox and Gui.List: " . (gb2_CZ_CB is Gui.ComboBox and gb2_CZ_CB is Gui.List) . "`n"
	s .= "Drop-down list with 4 rows is Gui.DDL and Gui.List: " . (MyDDL is Gui.DDL and MyDDL is Gui.List) . "`n"
	s .= "ListBox test is Gui.ListBox and Gui.List: " . (MyListBox is Gui.ListBox and MyListBox is Gui.List) . "`n"
	s .= "Tab is Gui.Tab and Gui.List: " . (Tab is Gui.Tab and Tab is Gui.List) . "`n"
	; Back to regular controls.
	s .= "MonthCal test is Gui.MonthCal: " . (MyMonthCal is Gui.MonthCal) . "`n"
	s .= "^ Use top menu is Gui.Pic: " . (SrchPic is Gui.Pic) . "`n"
	s .= "Progress bar is Gui.Progress: " . (MyProgress is Gui.Progress) . "`n"
	s .= "Radio group tests (1) is Gui.Radio: " . (RadioOne is Gui.Radio) . "`n"
	s .= "ControlSetText Test (RichEdit) is Gui.RichEdit: " . (SecondRichEdit is Gui.RichEdit) . "`n"
	s .= "Sliding test is Gui.Slider: " . (MySlider2 is Gui.Slider) . "`n"
	s .= "Status bar is Gui.StatusBar: " . (MySB is Gui.StatusBar) . "`n"
	s .= "Press Win-Z is Gui.Text: " . (Menu_Label is Gui.Text) . "`n"
	s .= "TreeView test is Gui.TreeView: " . (TV is Gui.TreeView) . "`n"
	s .= "UpDown test is Gui.UpDown: " . (nud is Gui.UpDown) . "`n"

	MsgBox(s)
}

^!9:: {
	GetPix()
}

Gui2 := Gui(,"Testing Child GUI")
Gui2.Opt("+Owner")

Gui2StyleButton := Gui2.Add("Button", ,"Style Button")
Gui2StyleButton.OnEvent("Click", "StyleTest")

Gui2GetControlsButton := Gui2.Add("Button", "xc+100 yp", "Get Ctrls")
Gui2GetControlsButton.OnEvent("Click", "GetTheControls")

Gui2FindCtrlsButton := Gui2.Add("Button", "xc+180 yp", "Enum Ctrls")
Gui2FindCtrlsButton.OnEvent("Click", "EnumCtrls")

Gui2CtrlIndexButton := Gui2.Add("Button", "xc+260 yp", "Find by _Item")
Gui2CtrlIndexButton.OnEvent("Click", "FindByItem")

Gui2Edit := Gui2.Add("Edit", "xc+10 y+20 h400 w500 +Multiline")
;MsgBox(Gui2Edit.Hwnd, "Hwnd of Edit")

SecondGUI(*) {
	Gui2.Show()
	ControlGetPos(&x, &y,,, Gui2Edit.Hwnd)
	Gui2Edit.Text := "Edit position: " . x . " " y
}

GetTheControls(*) {
	MyWords := Gui2Edit.Hwnd
	MyBtn1 := ControlGetClassNN(Gui2StyleButton.Hwnd)
	TheMsg := "The Style Button's ClassNN is " . MyBtn1 . "`n"
	TheMsg := TheMsg . "`nSecond button's Hwnd is " . Gui2GetControlsButton.Hwnd . "`n"

	TheMsg := TheMsg . "`nThe Main GUI's hwnd is " . MyGui.Hwnd

	ControlGetPos(&x, &y,,, Gui2FindCtrlsButton.Hwnd)
	TheMsg .= "`n`nFind button's position is " . x . ", " . y
	MsgBox(TheMsg, "Testing different methods of finding controls")
	Sleep(2000)
}

FindSecondGuiEdit(*) {
	; Called by button "Get Edit Hwnd"
	MyWords := Gui2Edit.Hwnd
	StyleBtn := ControlGetClassNN(Gui2StyleButton.Hwnd)
	TheOtherMsg := "The Style Button's ClassNN is " . StyleBtn
	TheOtherMsg := TheOtherMsg . "`nChild GUI Edit's hwnd is " . MyWords

	MsgBox(TheOtherMsg, "More testing of different methods to find controls")

}

EnumCtrls(*) {
	theMsg := ""
	For GuiCtrlObj in MyGui {
        try
        {
            theNN := ControlGetClassNN(GuiCtrlObj, MyGui) ; Sometimes it can't find a window and throws and error, so just catch and continue.
        }
        catch
        {
            continue
        }

	theMsg .= "Control #" A_Index " is " theNN "`n"
	}
	Gui2Edit.Value := theMsg
}

StyleTest(*)  {
	ToolTip("Setting style to -0xC00000`n(Will revert in two seconds to`n+0xC00000)")
	WinSetStyle("-0xC00000", "A")
	Sleep(2000)
	ToolTip
	WinSetStyle("+0xC00000", "A")
}

FindByItem(*) {
	EditObj := Gui2Edit
	MsgBox(EditObj.Text)
}

; GUI3

Gui3 := Gui(, "KEYSHARP TESTS")
Gui3.Name := "Howard"
ButtonOne := Gui3.Add("Button", "w200", "Find by Text")
ButtonOne.OnEvent("Click", "FindByText")
ButtonTwo := Gui3.Add("Button", "w200", "Find by Hwnd")
ButtonTwo.OnEvent("Click", "FindByHwnd")
;ButtonThree := Gui3.Add("Button", "w200", "Find by ClassNN")
;ButtonThree.OnEvent("Click", "FindByClassNN")
ButtonFour := Gui3.Add("Button", "w200", "Find by NetClassNN")
ButtonFour.OnEvent("Click", "FindByNetClassNN")
ButtonFive := Gui3.Add("Button", "w200", "Find by Name")
ButtonFive.OnEvent("Click", "FindByName")

ButtonDummy := Gui3.Add("Button", "w200", "Test Dummy")
ButtonDummy.Name := "I am a dummy button"
MyEdit3 := Gui3.Add("Edit", "xc+10 h200 w200")

HwndText := "Test Dummy button hwnd: " . ButtonDummy.Hwnd
MyEdit3.Value := HwndText

;Gui3.Show()

FindByText(*) {
	theItem := Gui3["Find by Name"]
	MsgBox("I found a button. Text:`n" theItem.Text, "Find by Text")
}

FindByHwnd(*) {
	theItem := Gui3[ButtonTwo.Hwnd]
	MsgBox("I found a button by its Hwnd. Text:`n" theItem.Text, "Find by Hwnd")
}

;FindByClassNN(*) {
;    theItem := Gui3["WindowsForms10.Button.app.0.5dbcd3_r3_ad11"]
;    MsgBox(theItem.Text, "Find by ClassNN")
;}

FindByNetClassNN(*) {
	theItem := Gui3["KeysharpButton5"]
	MsgBox("I found a button by its .NET classname. Text:`n" theItem.Text, "Find by NetClassNN")
}

FindByName(*) {
	theItem := Gui3[ButtonDummy.Name]
	MsgBox("I found a renamed button by Name.`nIt was renamed to:`n" theItem.Name, "Find by Name")
}

ThirdGUI(*) {
	Gui3.Show()
}

;MouseMoveTests(*) {
;    MsgBox("Dead monkey")
;}


MoveTheMouse(*) {
	mx :=
	my := 0
	CoordMode("Mouse", "Screen")
	MouseGetPos(&mx, &my)
	SendMode("Event")
	MouseMove(100,500,90)
	ToolTip("I'm at X:100, Y:500")
	Sleep(2000)
	MouseMove(1500,500,50)
	ToolTip("I'm here!")
	Sleep(2000)
	ToolTip()
	MouseMove(mx, my, 90)
	ToolTip("I'm back!")
	Sleep(2000)
	ToolTip()
}

#if WINDOWS
AddMsgMonitor(*)
{
	OnMessage 0x0201, "WM_LBUTTONDOWN"
}

RemoveMsgMonitor(*)
{
	OnMessage 0x0201, "WM_LBUTTONDOWN", 0
}
#endif

WM_LBUTTONDOWN(wParam, lParam, msg, hwnd)
{
	X := lParam & 0xFFFF
	Y := lParam >> 16
	Control := ""
	thisGui := GuiFromHwnd(hwnd)
	thisGuiControl := GuiCtrlFromHwnd(hwnd)

	if (thisGuiControl && (thisGuiControl.hwnd == CZ_Edit1.hwnd))
	{
		thisGui := thisGuiControl.Gui
		Control := "`n(in control " . thisGuiControl.ClassNN . ")"
		ToolTip "You left-clicked in Gui window '" thisGui.Title "' at client coordinates " X "x" Y "." Control
		SetTimer(() => ToolTip(), -2500)
	}
}

#if WINDOWS
WM_DROPFILES_CZ(wParam, lParam, msg, hwnd)
{
	; Only handle drops targeted at the ControlZoo edit control.
	if (hwnd != CZ_Edit1.Hwnd)
		return

	if !(ControlGetExStyle(CZ_Edit1.Hwnd) & 0x0010)
	{
		DllCall("DragFinish", "ptr", wParam)
		return
	}

	fileCount := DllCall("shell32\DragQueryFileW", "ptr", wParam, "uint", 0xFFFFFFFF, "ptr", 0, "uint", 0, "uint")
	paths := ""

	Loop fileCount
	{
		index := A_Index - 1
		chars := DllCall("shell32\DragQueryFileW", "ptr", wParam, "uint", index, "ptr", 0, "uint", 0, "uint") + 1
		buf := Buffer(chars * 2, 0)
		_ := DllCall("shell32\DragQueryFileW", "ptr", wParam, "uint", index, "ptr", buf, "uint", chars, "uint")
		path := StrGet(buf, "UTF-16")

		if (path != "")
			paths .= (paths != "" ? "`r`n" : "") . path
	}

	DllCall("shell32\DragFinish", "ptr", wParam)

	if (paths = "")
		return

	existing := ControlGetText(CZ_Edit1, MyGui)
	if (existing != "")
		paths := existing . "`r`n" . paths

	ControlSetText(paths, CZ_Edit1, MyGui)
}
#endif

;ReloaderBtn := MyGui.Add("Button", "w200 h25 xc+10 y+5", "Reload").OnEvent("Click", "Reload")

;ReloadMe(*) {
;    Reload()
;}

; ┌────────────────────────┐
; │  ControlZoo Functions  │
; └────────────────────────┘

AddFuchsia(*) {
	ControlAddItem("Fuchsia", CZ_ListBox)
}

AddWhite2(*) {
	ControlAddItem("White", gb2_CZ_CB)
}

DeleteFuchsia(*) {
	Try
	{
		FuchsiaIndex := ControlFindItem("Fuchsia", CZ_ListBox)
	}
	Catch as e  ; Handles the first error thrown by the block above.
	{
		MsgBox("An error was thrown!`nSpecifically: " e.Message, "ERROR!")
		Return
	}

	;MsgBox(FuchsiaIndex)
	ControlDeleteItem(FuchsiaIndex, CZ_ListBox)
}

DeleteWhite2(*) {
	Try
	{
		WhiteIndex := ControlFindItem("White", gb2_CZ_CB)
	}
	Catch as e  ; Handles the first error thrown by the block above.
	{
		MsgBox("An error was thrown!`nSpecifically: " e.Message, "ERROR!")
		Return
	}

	ControlDeleteItem(WhiteIndex, gb2_CZ_CB)
}

FuchsiaDeleteTrayTip(*) {
#if LINUX || OSX
	TrayTip("Also tests ControlFindItem`nClick into another edit box to dismiss this.")
#else
	TrayTip("Also tests ControlFindItem")
#endif
}

ChooseIndex(*) {
	ControlChooseIndex(7, CZ_ListBox)
}

ChooseString(*) {
	ControlChooseString("красный", CZ_ListBox)
}

ChooseString_CB(*) {
	ControlChooseString("Purple", gb2_CZ_CB)
}

GetChoice(*) {
	Try
	{
	Choice := ControlGetChoice(CZ_ListBox, MyGui)
	MsgBox(Choice, "Choice")
	}
		Catch as e  ; Handles the first error thrown by the block above.
	{
		MsgBox("You must select an item first.", "ERROR!")
		Return
	}
}

GetIndex(*) {
	Try
	{
	index := ControlGetIndex(CZ_ListBox, MyGui)
	MsgBox(index, "Index")
	}
		Catch as e  ; Handles the first error thrown by the block above.
	{
		MsgBox("You must select an item first.", "ERROR!")
		Return
	}
}

GetClassNN(*) {
	ClassNN := ControlGetClassNN(CZ_ListBox, MyGui)
	MsgBox(ClassNN, "ClassNN")
}

GetEnabled(*) {
	Result := ControlGetEnabled(CZ_LbBtn8, MyGui)
	MsgBox(Result, "'Disabled' Button State (1: enabled 0: disabled)")
	Result2 := ControlGetEnabled(CZ_LbBtn6, MyGui)
	MsgBox(Result2, "ClassNN Button State (1: enabled 0: disabled)")
}

SetEnabled(*)
{
	Result := ControlGetEnabled(CZ_LbBtn8, MyGui)
	ControlSetEnabled(!Result, CZ_LbBtn8, MyGui)
}

GetHwnd(*) {
	Result := ControlGetHwnd(CZ_ListBox, MyGui)
	MsgBox(Result, "Hwnd of ListBox")
}

GetText(*) {
	Result := ControlGetText(CZ_LbBtn8, MyGui)
	MsgBox(Result, "Text of Target Button")
}

HideButton(*) {
	ControlHide(CZ_LbBtn8, MyGui)
}

ShowButton(*) {
	ControlShow(CZ_LbBtn8, MyGui)
}

IsItHidden(*) {
	Result := ControlGetVisible(Cz_LbBtn8, MyGui)
	If (Result != 0) {
		Result := "Visible"
	} Else {
		Result := "Hidden"
	}
	MsgBox(Result, "Visible or Not?")
}

GetFocusCtrl(*) {
	Focused := ControlGetFocus(MyGui.Title)
	MsgBox(Focused, "Focused Control")
}

#if WINDOWS
ToggleEditExStyle(*) {
	CurrentExStyle := ControlGetExStyle(CZ_Edit1.Hwnd)
	Description := "0x0010 is WS_EX_ACCEPTFILES (accept dropped files)."

	if (CurrentExStyle & 0x0010) {
		ControlSetExStyle("-0x0010", CZ_Edit1)
		DllCall("shell32\DragAcceptFiles", "ptr", CZ_Edit1.Hwnd, "int", 0)
		NewExStyle := ControlGetExStyle(CZ_Edit1.Hwnd)
		MsgBox("Removed 0x0010 from CZ_Edit1.`n" . Description . "`nBefore: 0x" . Format("{1:X}", CurrentExStyle) . "`nNow: 0x" . Format("{1:X}", NewExStyle), "ControlSetExStyle")
	} else {
		ControlSetExStyle("+0x0010", CZ_Edit1)
		DllCall("shell32\DragAcceptFiles", "ptr", CZ_Edit1.Hwnd, "int", 1)
		NewExStyle := ControlGetExStyle(CZ_Edit1.Hwnd)
		MsgBox("Added 0x0010 to CZ_Edit1.`n" . Description . "`nBefore: 0x" . Format("{1:X}", CurrentExStyle) . "`nNow: 0x" . Format("{1:X}", NewExStyle), "ControlSetExStyle")
	}
}
#endif

GetCol(*) {
	CurrentCol := EditGetCurrentCol(CZ_Edit1, MyGui)
	MsgBox(CurrentCol, "Current Colum No.")
	CurrentCol := ""
}

GetLine(*) {
	CurrentLine := EditGetCurrentLine(CZ_Edit1, MyGui)
	MsgBox(CurrentLine, "Current Line No.")
	CurrentLine := ""
}

GetLineText(*) {
	CurrentLine := EditGetCurrentLine(CZ_Edit1, MyGui)
	CurrentLineText := EditGetLine(CurrentLine, CZ_Edit1, MyGui)
	MsgBox(CurrentLineText, "Current Line Text")
	CurrentLineText := "" ; Reset variable
}

GetSelectedText(*) {
	SelectedText := EditGetSelectedText(CZ_Edit1, MyGui)
	MsgBox(SelectedText, "Selected text in Edit")
	SelectedText := "" ; Reset variable
}

EditPaster(*) {
	EditPasted := "How now brown cow"
	EditPaste(EditPasted, CZ_Edit1, MyGui)
}

LV_Selected(*) {
	List := ListViewGetContent("Selected", LV2, MyGui)
	MsgBox(List, "LV Selected")
	List := ""
}

LV_Focused(*) {
	List := ListViewGetContent("Focused", LV2, MyGui)
	MsgBox(List, "LV Focused")
	List := ""
}

LV_Col1(*) {
	List := ListViewGetContent("Col1", LV2, MyGui)
	MsgBox(List, "LV Column 1")
	List := ""
}

LV_Count(*) {
	List := ListViewGetContent("Count", LV2, MyGui)
	MsgBox(List, "LV Row Count")
	List := ""
}

LV_CountSelected(*) {
	List := ListViewGetContent("Count Selected", LV2, MyGui)
	MsgBox(List, "LV Count Selected")
	List := ""
}

LV_CountFocused(*) {
	List := ListViewGetContent("Count Focused", LV2, MyGui)
	MsgBox(List, "LV Count Focused")
	List := ""
}

LV_CountCol(*) {
	List := ListViewGetContent("Count Col", LV2, MyGui)
	MsgBox(List, "LV Column Count")
	List := ""
}

Click_CB(*) {
#if WINDOWS
	Send("#r")  ; Open the Run dialog.
	WinWaitActive("ahk_class #32770")  ; Wait for the dialog to appear.
	ControlShowDropDown("ComboBox1")  ; Show the drop-down list. The second parameter is omitted so that the last found window is used.
	Sleep(2000)
	ControlHideDropDown("ComboBox1")  ; Hide the drop-down list.
	Sleep(1000)
	Send("{Esc}")  ; Close the Run dialog.
#endif
}

GetPix(*) {
	mx :=
	my := 0
	MouseGetPos(&mx, &my)
	MyColorText.Text := PixelGetColor(mx, my)
	ColorString := "Bold s12 c" MyColorText.Text
	ColorString := StrReplace(ColorString, "0x", "")
	MyColorText.SetFont(ColorString)
}

; PixelGetColor / PixelSearch against the on-tab colour swatch (Image tab). No helper window: the swatch's
; own control Hwnd gives the screen bounds, so PixelGetColor reads its centre and PixelSearch re-finds that
; exact colour inside those bounds — self-consistent regardless of the rendered shade.
RunPixelGetColorTest() {
	global pixelSwatch

	try {
		CoordMode("Pixel", "Screen")   ; WinGetPos returns screen coords, so read pixels in screen space too
		WinGetPos(&sx, &sy, &sw, &sh, "ahk_id " pixelSwatch.Hwnd)
		cx := sx + sw // 2
		cy := sy + sh // 2
		color := PixelGetColor(cx, cy)
		SetStatus("pixel_main", "Pixel status: PASS - PixelGetColor read " color " at swatch centre (" cx "," cy ")")
		AppendLog("PixelGetColor sampled (" cx "," cy ") -> " color)
	} catch as err {
		SetStatus("pixel_main", "Pixel status: FAIL - PixelGetColor: " err.Message)
		AppendLog("PixelGetColor test failed: " err.Message)
	}
}

RunPixelSearchTest() {
	global pixelSwatch

	try {
		CoordMode("Pixel", "Screen")   ; WinGetPos returns screen coords, so search pixels in screen space too
		WinGetPos(&sx, &sy, &sw, &sh, "ahk_id " pixelSwatch.Hwnd)
		cx := sx + sw // 2
		cy := sy + sh // 2
		color := PixelGetColor(cx, cy)
		if PixelSearch(&fx, &fy, sx, sy, sx + sw - 1, sy + sh - 1, color, 4) {
			SetStatus("pixel_main", "Pixel status: PASS - PixelSearch found " color " at " fx "," fy)
			AppendLog("PixelSearch found colour " color " at " fx "," fy " within the swatch bounds.")
		} else {
			SetStatus("pixel_main", "Pixel status: FAIL - PixelSearch did not find " color " in the swatch bounds")
			AppendLog("PixelSearch did not find colour " color " in the swatch bounds.")
		}
	} catch as err {
		SetStatus("pixel_main", "Pixel status: FAIL - PixelSearch: " err.Message)
		AppendLog("PixelSearch test failed: " err.Message)
	}
}

Click_LB_Items(*)
{
	global CZ_ListBox
	items := ControlGetItems(CZ_ListBox)
	MsgBox(items.Join("`n"))
}

Click_CB_Items(*)
{
	global gb2_CZ_CB
	items := ControlGetItems(gb2_CZ_CB)
	MsgBox(items.Join("`n"))
}

Click_CB_Show_Dropdown(*)
{
	ControlShowDropDown(gb2_CZ_CB, MyGui)
}

Click_CB_Hide_Dropdown(*)
{
	ControlHideDropDown(gb2_CZ_CB, MyGui)
}

LoadSC(*) {
	Tab.UseTab("Image")
	path := A_Desktop . A_DirSeparator . "MyScreenClip.png"
	If (!FileExist(path)) {
		Image.FromRect(100, 100, 200, 200).Save(path)
		Sleep(100)
	}
	MyThirdPic := LoadPicture(path)

	; Show the clip at its native captured size. Passing "w160 h160" would scale the 200x200 capture down to
	; 160x160 (AHK sizes a Picture to its explicit width/height); omit them so the clip displays 1:1.
	MyLoadedPic := MyGui.Add("Picture", "xc+90 yc+158 border", "HBITMAP:" MyThirdPic)
	Sleep(2000)

#if WINDOWS
	DllCall("DestroyWindow", "Ptr", MyLoadedPic.Hwnd)
#endif
	; Tab.UseTab()
	FileDelete(path)
	MyThirdPic := ""
	MyLoadedPic := ""
	MyThirdPic := ""
}

; ┌───────────────────────┐
; │  SEND & HOTKEY TESTS  │
; └───────────────────────┘



MyGui.UseGroup()
; The Send & Hotkey tab's manual tests: SendEvent/ControlSend/ControlSendText sit in the Send Variants group,
; and the Hotkey() registration tests (RCtrl+RShift -> AltTab + toggle, .INI hotkey + toggle, F3 Explorer
; selection) are in the "Manual Hotkey() registration" group. Their handler functions follow below.

; ┌────────────────────────────────────┐
; │  Send and Hotkey button functions  │
; └────────────────────────────────────┘

; SendEvent / ControlSend / ControlSendText now self-validate like the other Send buttons: clear the target
; edit, send a known string, then compare the resulting text (no MsgBox of what it "should" be).
BtnSendEventFunc(*) {
	global gSendTarget

	expected := "Typed via SendEvent"
	try {
		PrepareSendTarget()
		SendEvent("{Text}" expected)
		Sleep(250)
		actual := gSendTarget.Value
		if (actual = expected) {
			SetStatus("input_send", "SendEvent status: PASS")
			AppendLog("SendEvent typed the expected text into the suite-owned edit.")
		} else {
			SetStatus("input_send", "SendEvent status: FAIL")
			AppendLog("SendEvent mismatch. Expected <" expected "> but saw <" actual ">.")
		}
	} catch as err {
		SetStatus("input_send", "SendEvent status: BLOCKED/ERROR")
		AppendLog("SendEvent threw: " err.Message)
	}
}

BtnControlSendFunc(*) {
	global gSendTarget

	expected := "Typed via ControlSend"
	try {
		PrepareSendTarget()
		ControlSend("{Text}" expected, gSendTarget, MyGui)
		Sleep(250)
		actual := gSendTarget.Value
		if (actual = expected) {
			SetStatus("input_send", "ControlSend status: PASS")
			AppendLog("ControlSend produced the expected text in the suite-owned edit.")
		} else {
			SetStatus("input_send", "ControlSend status: FAIL")
			AppendLog("ControlSend mismatch. Expected <" expected "> but saw <" actual ">.")
		}
	} catch as err {
		SetStatus("input_send", "ControlSend status: BLOCKED/ERROR")
		AppendLog("ControlSend threw: " err.Message)
	}
}

BtnControlSendTextFunc(*) {
	global gSendTarget

	; ControlSendText is literal, so the braces must appear verbatim (not be interpreted).
	expected := "Literal {Blind}{Text} via ControlSendText"
	try {
		PrepareSendTarget()
		ControlSendText(expected, gSendTarget, MyGui)
		Sleep(250)
		actual := gSendTarget.Value
		if (actual = expected) {
			SetStatus("input_send", "ControlSendText status: PASS")
			AppendLog("ControlSendText produced the expected literal text in the suite-owned edit.")
		} else {
			SetStatus("input_send", "ControlSendText status: FAIL")
			AppendLog("ControlSendText mismatch. Expected <" expected "> but saw <" actual ">.")
		}
	} catch as err {
		SetStatus("input_send", "ControlSendText status: BLOCKED/ERROR")
		AppendLog("ControlSendText threw: " err.Message)
	}
}

; ┌─────────────────────────┐
; │  HOTKEY() TEST SECTION  │
; └─────────────────────────┘

; (The Hotkey() registration buttons are in the "Manual Hotkey() registration" group on the Send & Hotkey tab.)

; ┌────────────────────┐
; │  Hotkey functions  │
; └────────────────────┘
; (Func.Bind binding is covered by unit tests; only manual hotkey tests live here.)

StupidTrickTwo(*) {
	Hotkey("RCtrl & RShift", "AltTab")
}

; (The AltTab + Toggle buttons cover hotkey on/off; Func binding is covered by unit tests.)

ToggleHotkey(*) {
	Try
	{
		Hotkey("RCtrl & RShift", "Toggle")
	}
	Catch
	{
		MsgBox("Set the AltTab hotkeyfirst!", "ERROR", "T2")
	}

}

GrabFromIni(*) {
	HotkeyVal := IniRead("hotkeyini_1.ini", "HotkeyToRead", "Key")
	Hotkey(HotkeyVal, "AltTab")
}

ToggleFromIni(*) {
	Try
	{
		Hotkey("RCtrl & LShift", "Toggle")
	}
	Catch
	{
		MsgBox("Set the .INI hotkeyfirst!", "ERROR", "T2")
	}
}

#if WINDOWS

#HotIf WinActive('ahk_class CabinetWClass ahk_exe explorer.exe')
F3::MsgBox getSelected()
#HotIf

getSelected(*) { ; https://www.autohotkey.com/boards/viewtopic.php?style=17&t=60403#p255256 by teadrinker
	hwnd := WinExist('A'), selection := ''

	If WinGetClass() ~= '(Cabinet|Explore)WClass'
		For window in ComObject('Shell.Application').Windows
		{
			Try
				val := window.hwnd
			Catch
				Return

			If val = hwnd
				For item in window.document.SelectedItems
					selection .= item.Path '`n'
		}

	Return Trim(selection, '`n')
}

#endif

; ┌───────────────────────────┐
; │  FUNCTIONS AND CALLBACKS  │
; └───────────────────────────┘

LV_DoubleClick(LV, RowNumber)
{
	RowText := LV.GetText(RowNumber, 1)  ; Get the text from the row's first field.
	ColumnText := LV.GetText(RowNumber, 2)
	ToolTip("You double-clicked row number " RowNumber ". File '" RowText "' has size " ColumnText "kb.")
}

PopulateMainListView()
{
	global LV, LVFolder
	LV.Delete()

	Loop Files LVFolder . A_DirSeparator . "*.*"
		LV.Add(, A_LoopFileName, A_LoopFileSizeKB)
}

DirSelectForLV(*)
{
	global LVFolder
	selected := DirSelect(LVFolder, 0, "Choose a folder for the ListView")

	if (selected = "")
		return

	LVFolder := selected
	PopulateMainListView()
}

; ┌──────────────────────┐
; │  Change header font  │
; └──────────────────────┘

ChangeFont(*)
{
	global TEST_HEADER
	TEST_HEADER.SetFont("cBlue s14", "Comic Sans MS")
}
; ┌────────────────┐
; │  Restore font  │
; └────────────────┘

ChangeFontBack(*)
{
	TEST_HEADER.SetFont("cBlack s8", "Arial")
	MsgBox("Done", "Restoring Font")
}
; ┌───────────────────────────┐
; │  Change background color  │
; └───────────────────────────┘

ChangeBG(*)
{
	global origBackColor := MyGui.BackColor
	MsgBox(MyGui.BackColor, "Background color:")
	MyGui.BackColor := GuiBGColor
}
; ┌───────────────────────────────┐
; │  Restore background function  │
; └───────────────────────────────┘


RestoreBG(*)
{
	global MyGui, origBackColor
	MyGui.BackColor := origBackColor
}

; ┌───────────────────────┐
; │  Input test function  │
; └───────────────────────┘

InputTest(*) {
	OutputVar := InputBox("What is your first name?", "Question 1").Value
	if (OutputVar = "Bill")
		MsgBox("That's an awesome name, " OutputVar ".", "What a great name ...")

	OutputVar2 := InputBox("Do you like AutoHotkey?", "Question 2").Value
	if (OutputVar2 = "yes")
		MsgBox("Thank you for answering " OutputVar2 ", " OutputVar "! We will become great friends.", "You are in good company")
	else
		MsgBox(OutputVar ", That makes me sad.", "Sorry to hear it")
}


; ┌───────────────────────┐
; │  RadioThree callback  │
; └───────────────────────┘

RadioThreeClicked(*) {
	MsgBox("You clicked the last radio button.", "Radio 3 Clicked")
}

; ┌─────────────────────┐
; │  Checkbox callback  │
; └─────────────────────┘

CheckBoxOneClicked(*) {
	IsChecked := ControlGetChecked(CheckBoxOne, "KEYSHARP TESTS")
	MsgBox("1 is checked - 0 is unchecked`nTests 'ControlGetChecked' also`n`nValue is: " IsChecked, "Checkbox Test")
	TrayTip("TrayTipTest", "I will see myself out, thanks!", "Icon!")
	Sleep(1000)
	HideTrayTip()
}

SetChecked(*)
{
	ControlSetChecked(true, RadioThree, MyGui)
	ControlSetChecked(true, CheckBoxOne, MyGui)
}

SelectMenuByIndex(*)
{
	MenuSelect(MyGui, "KEYSHARP TESTS", "1&", "5&", "2&")
}

SelectByString(*)
{
	MenuSelect(MyGui, "KEYSHARP TESTS", "Menu Icon Test", "My Submenu", "Item B")
}

MinimizeBySystemMenu(*)
{
	MenuSelect(MyGui, "KEYSHARP TESTS", "0&", "Minimize")
}

; ┌─────────────────┐
; │  TreeView Edit  │
; └─────────────────┘
MyTreeView_Edit(TV, Item) {
	;MsgBox("Sort Not Implemented", "Men at Work")
	TV.Modify(TV.GetParent(Item), "Sort")  ; This works even if the item has no parent.
}

; ┌───────────────────────────────────────────────────────────────────────────┐
; │  https://www.autohotkey.com/board/topic/69784-different-tab-backgrounds/  │
; └───────────────────────────────────────────────────────────────────────────┘

; ┌────────────────┐
; │  Hide TrayTip  │
; └────────────────┘

; Copy this function into your script to use it.
HideTrayTip(*) {
	TrayTip()  ; Attempt to hide it the normal way.
	if SubStr(A_OSVersion,1,3) = "10." {
		A_IconHidden := True
		Sleep(200) ; It may be necessary to adjust this sleep.
		A_IconHidden := False
	}
}


; ┌──────────────────────────────┐
; │  Send Text to Edit Callback  │
; └──────────────────────────────┘

EditVar := "
(
A line of text.
By default, the hard carriage return (Enter) between the previous line and this one will be stored.
	This line is indented with a tab; by default, that tab will also be stored.
"Quote marks" are now automatically escaped when appropriate.
)"

SendTextToEdit(*) {
	global EditVar
	ControlSetText(EditVar, SecondEdit)
	Sleep(50)

	if (NormalizeNewlines(SecondEdit.Value) = NormalizeNewlines(EditVar)) {
		SetStatus("edits_settext", "ControlSetText -> Edit status: PASS")
		AppendLog("ControlSetText round-tripped the multi-line text into the edit.")
	} else {
		SetStatus("edits_settext", "ControlSetText -> Edit status: FAIL")
		AppendLog("ControlSetText mismatch: the edit content did not match the source text.")
	}
}

; ┌───────────────────────┐
; │  Clear Edit Callback  │
; └───────────────────────┘
ClearEdit(*) {
	ControlSetText("", SecondEdit)
}
; ┌──────────────────────┐
; │  RichEdit Callbacks  │
; └──────────────────────┘

SendTextToRichEdit(*) {
	;MsgBox(EditVar)
	; ControlSetText(EditVar, SecondRichEdit)
	global EditVar
	SecondRichEdit.Value := EditVar
}

SendRtfToRichEdit(*)
{
	RawRichEditVar := "
(
{\rtf1\ansi\ansicpg1252\deff0\nouicompat\deflang3081\deflangfe3081{\fonttbl{\f0\fswiss\fprq2\fcharset0 Calibri;}}
{\colortbl ;\red0\green0\blue255;\red5\green99\blue193;}
{\*\generator Riched20 10.0.19041}{\*\mmathPr\mnaryLim0\mdispDef1\mwrapIndent1440 }\viewkind4\uc1
\pard\widctlpar\sa160\sl252\slmult1\qc {\f0\fs22\lang2057{\field{\*\fldinst{HYPERLINK "https://github.com/dotnet/winforms/issues/146" }}{\fldrslt{\ul\cf1\cf2\ul Example}}}}\f0\fs22\lang2057  Document\par
\par

\pard\widctlpar\fi-360\li360\sa160\sl252\slmult1 1.\tab Section Title {{\field{\*\fldinst{HYPERLINK "http://www.google.com" }}{\fldrslt{\ul\cf1\cf2\ul www.google.com}}}}\f0\fs22  \par

\pard\widctlpar\fi-432\li792\sa160\sl252\slmult1 1.1.\tab  Some stuff\par

\pard\widctlpar\fi-504\li1224\sa160\sl252\slmult1 1.1.1.\tab  Some stuff\rquote s thing\par

\pard\widctlpar\fi-432\li792\sa160\sl252\slmult1 1.2.\tab  More stuff\par

\pard\widctlpar\fi-360\li360\sa160\sl252\slmult1 2.\tab Next Section\par

\pard\widctlpar\fi-432\li792\sa160\sl252\slmult1 2.1.\tab Other stuff\par

\pard\widctlpar\li720\sa160\sl252\slmult1\par
}
)"
SecondRichEdit.RichText := RawRichEditVar
}

; ┌───────────────────────┐
; │  Clear Edit Callback  │
; └───────────────────────┘
ClearRichEdit(*) {
	ControlSetText("", SecondRichEdit)
}

GetLineCount(*)
{
	; Populate the edit with a known, non-wrapping 3-line probe and confirm EditGetLineCount agrees.
	ControlSetText("Line one`r`nLine two`r`nLine three", SecondEdit)
	Sleep(50)
	count := EditGetLineCount(SecondEdit, MyGui)

	if (count = 3) {
		SetStatus("edits_linecount", "EditGetLineCount status: PASS (" count " lines)")
		AppendLog("EditGetLineCount returned 3 for the 3-line probe text.")
	} else {
		SetStatus("edits_linecount", "EditGetLineCount status: FAIL (got " count ", expected 3)")
		AppendLog("EditGetLineCount returned " count " for the 3-line probe text.")
	}
}

; ┌───────────┐
; │  LoadPic  │
; └───────────┘

MyFirstPic := ""
MySecondPic := ""
MyThirdPic := ""
Monkey := A_WorkingDir . A_DirSeparator . "monkey.ico"

#if WINDOWS
hSecondPic := GetIcon("W")
GetIcon(Theme, W:=0, H:=0)
{ ; v1.10
    Local B64, B64Len, nBytes, Bin

    B64 := ( Theme="W" ? "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAMAAACdt4HsAAAAXVBMVEXruAD//+KFZEL13HH+/NiPclLsuwr9+c789cH887v46Jj45pP02GbtvhPEt5"
    . "ruwh/67ar67amii22Zf2Dyz0rxz0nb1LfUyq7MwaSzooTuwiD499rx7tHn4sWrl3l1GphJAAABHUlEQVRYw+2WybLCIBBF3wUyCZmMUfMc/v8zLXeaC0IXW86+T9FAD3+FQiH"
    . "Gv7NTa0w7WTfKo3vd4YNO96LwWhvsMLpOjz818NCsqfEaAXTa8Q8IckhJ4zOeDdLzy7M4IcIauYDv+38qpa67t6glCVQkiCTRGxY8dgLT/zwAC+6Se+xYUGFP96P+wIIFRLg2"
    . "nUewgXBBgfUIBhA2KJg8giuIOShoPYIziDYoMB7BA4QRCW4CAadwVBUkKUwsWCC5RMuCIxib/pGeagPj0r/yZbiAGZOLyU8na4iMTm8oZ6WWO7ihJB9hUZ5i0oKmWnkETS1o6"
    . "5viYlolg+U20DPq3NGWPVwzx3vugpG74uQuWWJGZ+f3mje/17xCoRDhBWODDIHAQPWnAAAAAElFTkSuQmCC" : Theme="I" ? "iVBORw0KGgoAAAANSUhEUgAAAEAAAABAC"
    . "AMAAACdt4HsAAAAVFBMVEUzmf/w+P8ZRJmSyf/o9P/c5vOks9Pg8P/V6v/P6P/B4f+y2f+u1v+IxP9wuP9Npv9Eof88nv9tg7jH0ue7yOA6nf80VqFKZqmRosrR3O18kL9cdb"
    . "Au8FSxAAABHElEQVRYw+2WyZaEIAxFO4JhKMSpyp7+/z9bdzYRk2xqxd2/e+AkIXw0Gg2OcUjRW+tjGkZ9ejIBTgQzqeKzsVBgzSzPZwcXuCzNG6hgRPHXA6o8XgLBvzwxKM/"
    . "f/3bdortFhjNf3c4TzmSmfo4TuFlTANzzm1WUYir7BxH7sqMm+QH03RAkgnAzfyCiPpuDTDBUBQkKfkgVD1JVEKFguxTEqsBDQbfzTQS+KrBXgp4IrFjQHwJQCPyF4BMUV4gy"
    . "QbwpIxUsVJDEjbQSAdNIo0wwMsPECQIzzqzAMA8KJ7C3S86QKmy67TI70onrE5E8qnUymYWimbJqsSxHfENyAfFq6xFxFa82/XLVr/d3fTD0Xxz9J0v/zWs0Ggx/07kMr8YqP"
    . "fYAAAAASUVORK5CYII=" : Theme="S" ? "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAMAAACdt4HsAAAAY1BMVEVCkwDw/+kVVySZyXXo+t/c7dbh9tTS7MHF5K+325yz2"
    . "ZiQxGl6tktRnBRKmAvG28Jrj24yZzujvaHm9uBaoiDR5MzX78fW78avx62WspWJp4lbg19IdU5IlwlboiG70bd7m3xysmd2AAABTUlEQVRYw+2W226EIBCGOwMqKyqeD6tr+/"
    . "5PWeO2oSjEsdzyJV5o8v+ZgzDzEQgErlhlKnrOe5HK9r46Zwn8IWH5LXnBOBzgrKDrsxgsxBlVz8ABI8lfD3DyeBEMtN7m4BE/MYsMLsgu+heDFTWVXz+9KP6TwIiIMyWJnIO"
    . "NqEKsInjD8/sBlIhYk+qYOBMo9Wvi1q9go6kQMQKN+2xKsFEbCWxIp0FqreCmX4wvqdNAgIUnIn4aX4TToHcEUILB4DTgjgAUGHCSQaQDeIIJJ6TQLVh1vy2IwGQgFHFCfDtU"
    . "5wBAENrYzIg4NzDqClDaKMF0mKDULaD8SOvhBGK9PSMcaWmHSeHOctIn1OM87QY1HGHkC2VBo4f6QqGGoNBWQnbjUu2Uak4DrvC91n0Hi+9o8x6uvuPdf8HwX3H8lyw6rUzFw"
    . "Pmwr3mBQOCCb5bODse1hzUgAAAAAElFTkSuQmCC" : Theme="E" ? "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAMAAACdt4HsAAAAS1BMVEXkAAD/7u1yHCTyd3eziovFo"
    . "6T+5OPlCgqfbW/92dj7y8v6xcT4s7LOr6/2m5rwa2voISHmFRT2oJ/tTU3tTUy8l5ipfH6UXWD2oaDGo2xeAAABGUlEQVRYw+2W3W7DIAyFdyCB8VMIdN32/k+6SZ3SZBSbyr"
    . "d8d1F0TmxiG79NJhOOLafojfEx5e11ddEBB4IuL8mtNviH0XZcf3V4gvsY1Wt00GPhV3SpI2l8g6AK4h/M4goG5iStA4OzfAKCJMqjfpYLjqzrXlFlKIBFqctRr9Q6EkI4CP4"
    . "c2sdA9B923pV6fPSmfln2d/3ezHjqsJz1yF2DhNah1SN1DSJah1aP2DXwaB2+Gj1818DgzKe6c8MJM2yARd3TGDXwtAGfQqRT4A8xjR1ioguJ/42ZLmW+kDaymfhSDlQ7t/rW"
    . "QZMDhW9nU8iRxg8UzQxVbqQ5Kx3r4ouFpYKgii9X4fUuXTCkK45wyRKueZPJhOEHEsMMXLgJ8a8AAAAASUVORK5CYII=" : "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYA"
    . "AAAfFcSJAAAADUlEQVQImWOor69nBgAEfwGBGWiMoAAAAABJRU5ErkJggg==" )

    nBytes :=  Floor((B64Len := StrLen(B64 := RTrim(B64,"=")))*3/4)
    Bin    :=  Buffer(nBytes)

    DllCall("Crypt32.dll\CryptStringToBinary", "str",B64, "int",B64Len, "int",1, "ptr",Bin,"uintp",nBytes, "Int",0, "Int",0)
    Return DllCall("User32.dll\CreateIconFromResourceEx", "ptr",Bin, "int",nBytes, "int",1, "int",0x30000, "Int",W, "Int",H, "Int",0, "ptr")
}

Icon2 := "HICON:*" . hSecondPic ; The * is important so it can be reused.
; svgToHBITMAP is X64-only: it hands D2D1_SIZE_F (two floats) to CreateSvgDocument as a packed
; Uint64, which is how the x64 ABI passes an 8-byte struct by value. On ARM64 that struct is a
; homogeneous float aggregate passed in v0/v1 instead, so the packed integer lands in the wrong
; register and the call faults. DllCall/ComCall cannot express struct-by-value, so there is nothing
; to pass here that would be right on both.
#if X64
Icon3 := "HBITMAP:*" svgToHBITMAP(A_WorkingDir . A_DirSeparator . "check-mark.svg", 100, 100)
#endif
#endif

LoadPic(*) {
	global
	local x, y, w, h
	Tab.UseTab("Image")

	if (MyFirstPic = "")
		MyFirstPic := MyGui.Add("Picture", "xc+10 yc+410 w100 h-1 border", Monkey)
	else
		MyFirstPic.Value := Monkey
#if WINDOWS
	if (MySecondPic = "")
		MySecondPic := MyGui.Add("Picture", "xc+120 yc+410 w100 h-1 border", Icon2)
	else
		MySecondPic.Value := Icon2

#if X64
	if (MyThirdPic = "")
		MyThirdPic := MyGui.Add("Picture", "xc+230 yc+410 w100 h-1 border", Icon3)
	else
		MyThirdPic.Value := Icon3
#endif
#endif
	Sleep(2000)
	MyFirstPic.Value := ""
#if WINDOWS
	MySecondPic.Value := ""
#if X64
	MyThirdPic.Value := ""
#endif
#endif
	Tab.UseTab()
	; MyGui.Opts("+Redraw")
}

#if WINDOWS
DestroyPic(*)
{
	global MyFirstPic, MySecondPic, MyThirdPic
	DllCall("DestroyWindow", "Ptr", MyFirstPic.Hwnd)
	DllCall("DestroyWindow", "Ptr", MySecondPic.Hwnd)
	MyFirstPic := ""
	MySecondPic := ""
#if X64
	DllCall("DestroyWindow", "Ptr", MyThirdPic.Hwnd)
	MyThirdPic := ""
#endif
}

; https://www.autohotkey.com/boards/viewtopic.php?f=83&t=121834
svgToHBITMAP(svgPath,width,height) {
	;https://gist.github.com/smourier/5b770d32043121d477a8079ef6be0995
	;https://stackoverflow.com/questions/75917247/convert-svg-files-to-bitmap-using-direct2d-in-mfc#75935717
	; ID2D1DeviceContext5::CreateSvgDocument is the carrying api
	hModule:=DllCall("GetModuleHandleA","AStr","WindowsCodecs.dll","Ptr")||DllCall("LoadLibraryA","AStr","WindowsCodecs.dll","Ptr")
	CLSID_WICImagingFactory:=Buffer(0x10)
	NumPut("UInt64",0x433D5F24317D06E8,CLSID_WICImagingFactory,0x0)
	NumPut("UInt64",0xC2ABD868CE79F7BD,CLSID_WICImagingFactory,0x8)
	IID_IClassFactory:=Buffer(0x10)
	NumPut("UInt64",0x0000000000000001,IID_IClassFactory,0x0)
	NumPut("UInt64",0x46000000000000C0,IID_IClassFactory,0x8)
	DllGetClassObject:=DllCall("GetProcAddress","Ptr",hModule,"AStr","DllGetClassObject","Ptr")
	DllCall(DllGetClassObject,"Ptr",CLSID_WICImagingFactory,"Ptr",IID_IClassFactory,"Ptr*",&IClassFactory:=0)

	IID_IWICImagingFactory:=Buffer(0x10)
	NumPut("UInt64",0x4314C395EC5EC8A9,IID_IWICImagingFactory,0x0)
	NumPut("UInt64",0x70FF35A9D754779C,IID_IWICImagingFactory,0x8)
	ComCall(3,IClassFactory,"Ptr",0,"Ptr",IID_IWICImagingFactory,"Ptr*",&IWICImagingFactory:=0) ;HRESULT IClassFactory::CreateInstance(IUnknown *pUnkOuter,REFIID riid,void **ppvObject)

	GUID_WICPixelFormat32bppPBGRA:=Buffer(0x10)
	NumPut("UInt64",0x4BFE4E036FDDC324,GUID_WICPixelFormat32bppPBGRA,0x0)
	NumPut("UInt64",0x10C98D76773D85B1,GUID_WICPixelFormat32bppPBGRA,0x8)
	ComCall(17,IWICImagingFactory,"Uint",width,"Uint",height,"Ptr",GUID_WICPixelFormat32bppPBGRA,"Int",0x2,"Ptr*",&IWICBitmap:=0) ;HRESULT IWICImagingFactory::CreateBitmap(UINT uiWidth,UINT uiHeight,REFWICPixelFormatGUID pixelFormat,WICBitmapCreateCacheOption option,IWICBitmap **ppIBitmap); 0x2=WICBitmapCacheOnLoad


	IID_ID2D1Factory:=Buffer(0x10)
	NumPut("UInt64",0x465A6F5006152247,IID_ID2D1Factory,0x0)
	NumPut("UInt64",0x07603BFD8B114592,IID_ID2D1Factory,0x8)

	DllCall("GetModuleHandleA", "AStr", "d2d1") || DllCall("LoadLibraryA", "AStr", "d2d1") ;this is needed to avoid "Critical Error: Invalid memory read/write"
	DllCall("d2d1\D2D1CreateFactory","Int",0,"Ptr",IID_ID2D1Factory,"Ptr",0,"Ptr*",&ID2D1Factory:=0) ;Int 0=D2D1_FACTORY_TYPE_SINGLE_THREADED

	D2D1_RENDER_TARGET_PROPERTIES:=Buffer(0x1c,0)
	ComCall(13,ID2D1Factory,"Ptr",IWICBitmap,"Ptr",D2D1_RENDER_TARGET_PROPERTIES,"Ptr*",&ID2D1RenderTarget:=0) ;HRESULT ID2D1Factory::CreateWicBitmapRenderTarget(IWICBitmap *target,D2D1_RENDER_TARGET_PROPERTIES &renderTargetProperties,ID2D1RenderTarget **renderTarget)

	; IID_ID2D1DeviceContext5:=Buffer(0x10)
	; NumPut("UInt64",0x4DF668CC7836D248,IID_ID2D1DeviceContext5,0x0)
	; NumPut("UInt64",0xB72EF61B99DEE8B9,IID_ID2D1DeviceContext5,0x8)
	; ComCall(0,ID2D1RenderTarget,"Ptr",IID_ID2D1DeviceContext5,"Ptr*",&ID2D1DeviceContext5:=0) ;HRESULT ID2D1RenderTarget::QueryInterface(REFIID riid,void **ppvObject)

	DllCall("shlwapi\SHCreateStreamOnFileW","WStr",svgPath,"Uint",0,"Ptr*",&IStream:=0)

	D2D1_SIZE_F:=Buffer(8)
	NumPut("float",width,D2D1_SIZE_F,0x0)
	NumPut("float",height,D2D1_SIZE_F,0x4)
	ComCall(115,ID2D1RenderTarget,"Ptr",IStream,"Uint64",NumGet(D2D1_SIZE_F,"Uint64"),"Ptr*",&ID2D1SvgDocument:=0) ;HRESULT ID2D1DeviceContext5::CreateSvgDocument(IStream *inputXmlStream,D2D1_SIZE_F viewportSize,ID2D1SvgDocument **svgDocument)

	; Clear the render target to opaque white first — the SVG path uses the default black fill, so on the
	; transparent (zero-initialized) WIC bitmap it would otherwise come out as an all-black box.
	clearColor := Buffer(16)
	NumPut("float",1.0,"float",1.0,"float",1.0,"float",1.0,clearColor)
	ComCall(48,ID2D1RenderTarget,"int") ;void ID2D1RenderTarget::BeginDraw()
	ComCall(47,ID2D1RenderTarget,"Ptr",clearColor,"int") ;void ID2D1RenderTarget::Clear(const D2D1_COLOR_F *clearColor)
	ComCall(116,ID2D1RenderTarget,"Ptr",ID2D1SvgDocument,"int") ;void ID2D1DeviceContext5::DrawSvgDocument(ID2D1SvgDocument *svgDocument)
	ComCall(49,ID2D1RenderTarget,"Ptr",0,"Ptr",0) ;HRESULT ID2D1RenderTarget::EndDraw(D2D1_TAG *tag1,D2D1_TAG *tag2)

	cbStride:=4*width ;stride=bpp*width
	pData:=Buffer(cbStride * height) ;bpp*width*height
	ComCall(7,IWICBitmap,"Ptr",0,"Uint",cbStride,"Uint",pData.Size,"Ptr",pData) ;HRESULT IWICBitmapSource::CopyPixels(WICRect *prc,UINT cbStride,UINT cbBufferSize,BYTE *pbBuffer)

	HBITMAP := DllCall("gdi32\CreateBitmap","Int",width,"Int",height,"Uint",1,"Uint",32,"Ptr",pData,"Ptr")
	return HBITMAP
}
#endif


; ┌────────────────────┐
; │  Listbox Callback  │
; └────────────────────┘

ListBoxClicked(*) {
	; MsgBox(MyListBox.Text, "ListBox")
	;MySB.SetIcon("Shell32.dll", 2)
	; MsgBox("Icon lives at " . A_KeysharpCorePath)
	MySB.SetIcon(A_KeysharpCorePath, "Keysharp.ico")
	MySB.SetFont("Norm cBlack")   ; clear any leftover green/red bold from a PASS/FAIL verdict
	MySB.SetText(MyListBox.Text . " selected in ListBox")
}

; ┌─────────────────────┐
; │  Multi LB Callback  │
; └─────────────────────┘
MultiLBClicked(*) {
	For Index, Field in MyMultiLB.Text
		{
			MsgBox("Selection number " Index " is " Field, "Multi ListBox")
		}
}

; ┌─────────────────────┐
; │  DropDown Callback  │
; └─────────────────────┘
DDLClicked(*) {
	MsgBox(MyDDL.Text, "Drop Down List")
}

; ┌─────────────────────┐
; │  ComboBox Callback  │
; └─────────────────────┘
CB_ButtonClicked(*) {
	MsgBox(MyCB.Text, "CB Selection")
}

; ┌─────────────────────────────┐
; │  Progress Button Callbacks  │
; └─────────────────────────────┘

Pbtn1Clicked(*) {
	;MsgBox(MyProgress.Value)
	MyProgress.Value -= 10
	MyVertProgress.Value -= 10
	ProgressStatusText.Value := "Values: " . MyProgress.Value . " " . MyVertProgress.Value
}

Pbtn2Clicked(*) {
	MyProgress.Value += 10
	MyVertProgress.Value += 10
	ProgressStatusText.Value := "Values: " . MyProgress.Value . " " . MyVertProgress.Value
}

MC_Colors(*) {
	MsgBox("Not implemented.", "Future feature")
}

; ┌─────────────────────┐
; │  Test GuiCtrl.Hwnd  │
; └─────────────────────┘

ShowEditHwnd(*) {
	MsgBox(HwndSecondEdit, "Test 'GuiCtrl.Hwnd'")
}

; ┌──────────────┐
; │  Update OSD  │
; └──────────────┘

UpdateOSD(*)
{
	mx :=
	my :=
	msx :=
	msy := 0
	prevMode := A_CoordModeMouse
	MouseGetPos(&mx, &my)
	CoordMode("Mouse", "Screen")
	MouseGetPos(&msx, &msy)
	CoordMode("Mouse", prevMode)
	CoordText.Text := ("X: " mx " Y: " my . " (" . msx . ", " . msy . ")")
}

; ┌────────────────────────────┐
; │  GroupBox Tab - Functions  │
; └────────────────────────────┘

SendToGB3(*) {
GB3Text := "
(
This uses 'ControlSetText' from a button in GroupBox 4 to populate this edit.
The first message box shows the Hwnd of this Edit.
The second message box shows '1' (True) if GuiCtrlFromHwnd created an
object from the Hwnd.
Finally, ControlSetText operates on the Object created from the Hwnd.
)"
	MsgBox(gb3Hwnd, "Hwnd of Groupbox 3 Edit")
	obj := GuiCtrlFromHwnd(gb3Hwnd)
	Result := IsObject(obj)
	MsgBox(Result, "If '1', the control is an Object")
	ControlSetText(StrReplace(GB3Text, "`n", A_NewLine), obj)
}

ClearGB3(*) {
	ControlSetText("", gb3Edit)
}

StartEditToolTip(*) {
ToolTipText := "
(
This uses 'ControlSetText' from a button in GroupBox 4 to populate this edit.
The first message box shows the Hwnd of this Edit.
The second message box shows '1' (True) if GuiCtrlFromHwnd created an
object from the Hwnd.
Finally, ControlSetText operates on the Object created from the Hwnd.
)"
	ToolTip(ToolTipText)
}

StopToolTip(*) {
	ToolTip()
}

; ┌───────────────────────────────┐
; │  Tab One Group Two functions  │
; └───────────────────────────────┘
Set_Style(*) {
	WinSetStyle("-0xC00000", "A")
}

Reset_Style(*) {
	WinSetStyle("+0xC00000", "A")
}

Set_Edit_Style(*)
{
#if WINDOWS
	ControlSetStyle("+0x8", HwndMyEdit)   ; 0x8 = ES_UPPERCASE
	ControlFocus(HwndMyEdit)

	if (ControlGetStyle(HwndMyEdit) & 0x8) {
		SetStatus("edits_style", "Uppercase ControlSetStyle status: PASS (ES_UPPERCASE set; type to see uppercasing)")
		AppendLog("ControlSetStyle applied ES_UPPERCASE to the edit.")
	} else {
		SetStatus("edits_style", "Uppercase ControlSetStyle status: FAIL")
		AppendLog("ControlSetStyle did not set ES_UPPERCASE on the edit.")
	}
#else
	MyEdit2.Opt("+Uppercase")
	HwndMyEdit := MyEdit2.Hwnd
	ControlFocus(HwndMyEdit)
#endif
}

Reset_Edit_Style(*)
{
#if WINDOWS
	ControlSetStyle("-0x8", HwndMyEdit)
	ControlFocus(HwndMyEdit)

	if !(ControlGetStyle(HwndMyEdit) & 0x8) {
		SetStatus("edits_style", "Reset edit style status: PASS (ES_UPPERCASE cleared)")
		AppendLog("ControlSetStyle cleared ES_UPPERCASE from the edit.")
	} else {
		SetStatus("edits_style", "Reset edit style status: FAIL")
		AppendLog("ControlSetStyle did not clear ES_UPPERCASE from the edit.")
	}
#else
	MyEdit2.Opt("-Uppercase")
	HwndMyEdit := MyEdit2.Hwnd
	ControlFocus(HwndMyEdit)
#endif
}

; ┌──────────────────────┐
; │  Move Gui functions  │
; └──────────────────────┘

MoveGui(*) {
	global winposx, winposy, winposw, winposh
	WinGetPos(&winposx, &winposy, &winposw, &winposh, MyGui)
	WinMove(100, 100, , , MyGui)
}

MoveGuiBack(*) {
	global winposx, winposy, winposw, winposh
	WinMove(winposx, winposy, winposw, winposh, MyGui)
}

; ┌──────────────────────────┐
; │  Image Search functions  │
; └──────────────────────────┘

; Checks that a live screen capture agrees with the Gui's own coordinate system. That is the one thing the
; headless tests cannot reach: ImageTests (ImageSearchFindsSubImage and friends) already cover the matcher
; against rich pixel content in memory, and ScreenTests.ImageSearch already covers capturing a region and
; finding it again, so neither is repeated here.
;
; The needle is authored below rather than loaded from killbill.png, and it is deliberately a block of one
; colour. A Picture control does NOT put an image file's own pixels on screen: macOS renders the image at
; the display's backing scale and converts it into the display's colour profile (Wayland scales it too), so
; a needle read from the file can never match its own on-screen rendering - not at any variation, and not
; with *w/*h scaling. Gui-drawn solid colours do survive the capture byte-for-byte, and a solid block stays
; solid however the display scales it, so this needle is valid at 1x, 2x and fractional scaling without the
; script having to know the scale.
ImgSrch(*) {
	global MyGui, pixelSwatch

	CoordMode("Pixel", "Screen")  ; the rects below come from WinGetPos, which reports screen coordinates

	; Every rectangle here comes from WinGetPos, so window, swatch and search result are all in ONE
	; coordinate space - the platform's screen space, which is what ImageSearch reports in too. Mixing in
	; Gui.GetClientPos / Control.GetPos does not work: those return the control's authored position in the
	; window's DPI-LOGICAL units (and GetClientPos likewise reports a logical size next to a physical
	; origin), so on a scaled Windows display a screen origin plus a logical offset lands far from the
	; control - and a logical width shrinks the search rectangle to a fraction of the window.
	try {
		WinGetPos(&winX, &winY, &winW, &winH, MyGui)
		WinGetPos(&swatchX, &swatchY, &swatchW, &swatchH, "ahk_id " pixelSwatch.Hwnd)

		needle := Image.Create(8, 8, 0xCC5533).ToBitmap()
		resultX := "", resultY := ""

		; Bounded to the window rather than the whole desktop, so a same-coloured pixel in some other
		; window cannot win the top-left-first scan. It also exercises the search-rectangle path.
		if !ImageSearch(&resultX, &resultY, winX, winY, winX + winW, winY + winH, "HBITMAP:" needle) {
			SetStatus("image_main", "Image status: FAIL - colour swatch not found inside the window")
			AppendLog("ImageSearch did not find the swatch fixture within the Gui window rect.")
			return
		}

		; Two independent checks on the reported position. It must be the swatch's own top-left corner as
		; the window functions report it, and reading that point back must give the colour searched for -
		; PixelGetColor maps screen->pixel where ImageSearch mapped pixel->screen, so a HiDPI scale slip
		; breaks one or both. The swatch is drawn with a border, whose width is the platform's business
		; (none on some, a pixel or two on others), so the solid block starts just inside the control's
		; rect: allow that inset rather than hard-coding a border width. A coordinate-space slip misses by
		; far more than a few pixels, so the check stays meaningful.
		inset := 4
		colour := PixelGetColor(resultX, resultY)
		atCornerX := resultX >= swatchX && resultX <= swatchX + inset
		atCornerY := resultY >= swatchY && resultY <= swatchY + inset

		if atCornerX && atCornerY && colour = 0xCC5533 {
			SetStatus("image_main", "Image status: PASS - swatch found at " resultX "," resultY " (matches WinGetPos)")
			AppendLog("ImageSearch found the swatch at " resultX "," resultY ", matching the control's screen rect at " swatchX "," swatchY ", and PixelGetColor there reads " colour ".")
		} else {
			SetStatus("image_main", "Image status: FAIL - found " resultX "," resultY " " colour ", expected " swatchX "," swatchY " (+" inset ") 0xCC5533")
			AppendLog("ImageSearch reported " resultX "," resultY " but the swatch's screen rect starts at " swatchX "," swatchY "; PixelGetColor there reads " colour ".")
		}
	} catch as e {
		SetStatus("image_main", "Image status: FAIL - ImageSearch error: " e.Message)
		AppendLog("ImageSearch threw: " e.Message)
	}
}

#if WINDOWS
; ┌──────────────────────────┐
; │  Hotkeys with DllCall()  │
; └──────────────────────────┘

F1::
F1 up::
{
	static SPI_GETMOUSESPEED := 0x70
	static SPI_SETMOUSESPEED := 0x71
	static OrigMouseSpeed := 0

	switch ThisHotkey
	{
	case "F1":
		; Retrieve the current speed so that it can be restored later:
		DllCall("SystemParametersInfo", "UInt", SPI_GETMOUSESPEED, "UInt", 0, "Ptr*", &OrigMouseSpeed, "UInt", 0)
		; Now set the mouse to the slower speed specified in the next-to-last parameter (the range is 1-20, 10 is default):
		DllCall("SystemParametersInfo", "UInt", SPI_SETMOUSESPEED, "UInt", 0, "Ptr", 3, "UInt", 0)
		KeyWait("F1") ; This prevents keyboard auto-repeat from doing the DllCall repeatedly.

	case "F1 up":
		DllCall("SystemParametersInfo", "UInt", SPI_SETMOUSESPEED, "UInt", 0, "Ptr", OrigMouseSpeed, "UInt", 0)  ; Restore the original speed.
	}
}

; ┌──────────────────┐
; │  Dll & COM Tab   │
; └──────────────────┘

MyGui.UseGroup()
Tab.UseTab("Dll && COM")

hideCursorDllLabel := MyGui.Add("Text", "w400 xc+10 y+10 cBlue S10","Press Win+C to hide the cursor, and press again to restore it.")

dllMsgBoxBtn := MyGui.Add("Button", "xc+10 y+10", "Dll MsgBox()")
dllMsgBoxBtn.OnEvent("Click", "DllMsgBox")

dllMsgBoxBtn := MyGui.Add("Button", "xc+10 y+10", "Dll IsWindowVisible() (run notepad then click this)")
dllMsgBoxBtn.OnEvent("Click", "DllIsWindowVisible")

dllWsprintfBtn := MyGui.Add("Button", "xc+10 y+10", "Dll wsprintf()")
dllWsprintfBtn.OnEvent("Click", "DllWsprintf")

dllPerformanceCounterBtn := MyGui.Add("Button", "xc+10 y+10", "Dll QueryPerformanceCounter()")
dllPerformanceCounterBtn.OnEvent("Click", "DllPerformanceCounter")

dllDllGetWindowRectBtn := MyGui.Add("Button", "xc+10 y+10", "Dll GetWindowRect()")
dllDllGetWindowRectBtn.OnEvent("Click", "DllGetWindowRect")

dllDllFillRectBtn := MyGui.Add("Button", "xc+10 y+10", "Dll FillRect()")
dllDllFillRectBtn.OnEvent("Click", "DllFillRect")

dllDllRemoveFromTaskbarBtn := MyGui.Add("Button", "xc+10 y+10", "Dll DeleteFromTaskbar() (clear for 3 seconds, then re-add)")
dllDllRemoveFromTaskbarBtn.OnEvent("Click", "DllDeleteFromTaskbar")

comDllRemoveFromTaskbarBtn := MyGui.Add("Button", "xc+10 y+10", "COM DeleteFromTaskbar() (clear for 3 seconds, then re-add)")
comDllRemoveFromTaskbarBtn.OnEvent("Click", "ComDeleteFromTaskbar")

comDllRunWordBtn := MyGui.Add("Button", "xc+10 y+10", "COM run MS Word")
comDllRunWordBtn.OnEvent("Click", "ComRunWord")

comDllRunWordListenerBtn := MyGui.Add("Button", "xc+10 y+10", "COM run MS Word with event listener")
comDllRunWordListenerBtn.OnEvent("Click", "ComRunWordEventListener")

comShellRunNotepad := MyGui.Add("Button", "xc+10 y+10", "COM shell Run() Notepad")
comShellRunNotepad.OnEvent("Click", "ComRunNotepadShell")

comShellExecNotepad := MyGui.Add("Button", "xc+10 y+10", "COM shell Exec() Notepad")
comShellExecNotepad.OnEvent("Click", "ComExecNotepadShell")

comFakeComCall := MyGui.Add("Button", "xc+10 y+10", "Fake COM call (hello)")
comFakeComCall.OnEvent("Click", "FakeComCall")

_ := MyGui.Add("Text", "xc+10 y+10 cBlue S10", "An animated Odie should appear below using ActiveX.")

axPic := "http://www.animatedgif.net/cartoons/A_5odie_e0.gif"
axText := "mshtml:<img src='" . axPic . "' />"
activeXOdie := MyGui.AddActiveX("w100 h150 xc+10 y+10", axText)

DllMsgBox(*)
{
	WhichButton := DllCall("MessageBox", "Int", 0, "Str", "Press Yes or No", "Str", "Title of box", "Int", 4)
	MsgBox "You pressed button #" WhichButton
}

DllIsWindowVisible(*)
{
	DetectHiddenWindows True
	if not DllCall("IsWindowVisible", "Ptr", WinExist("Untitled - Notepad"))  ; WinExist returns an Hwnd.
		MsgBox "Notepad is not visible."
	else
		MsgBox "Notepad is visible."
	DetectHiddenWindows False
}

DllWsprintf(*)
{
	ZeroPaddedNumber := Buffer(20)  ; Ensure the buffer is large enough to accept the new string.
	DllCall("wsprintf", "Ptr", ZeroPaddedNumber, "Str", "%010d", "Int", 432, "Cdecl")  ; Requires the Cdecl calling convention.
	strfmt := FormatCs("{1:0000000000}", 432)
	str := "Value from wsprintf(): " . StrGet(ZeroPaddedNumber) . "`n" . "Value from FormatCs(): " . strfmt . "`n" . "Reference value: 0000000432"
	MsgBox(str)
}

DllPerformanceCounter(*)
{
	freq := 0
	CounterBefore := 0
	CounterAfter := 0
	start := A_NowMs
	startTick := A_TickCount

	DllCall("QueryPerformanceFrequency", "Int64*", &freq)
	DllCall("QueryPerformanceCounter", "Int64*", &CounterBefore)
	Sleep(1000)
	DllCall("QueryPerformanceCounter", "Int64*", &CounterAfter)
	end := A_NowMs
	endTick := A_TickCount
	elapsed := (CounterAfter - CounterBefore) / freq * 1000
	diff := DateDiff(end, start, "L")
	elapsedTick := endTick - startTick
	MsgBox("This value should be near 1000ms: " . elapsed . "`r`nValue using DateDiff(): " . diff . "`r`nValue using A_TickCount: " . elapsedTick)
}

DllGetWindowRect(*)
{
	Run "Notepad"
	notepadHwnd := WinWait("Untitled - Notepad")  ; This also sets the "last found window" for use with WinExist below.
	Sleep(1000)
	Rect := Buffer(16)  ; A RECT is a struct consisting of four 32-bit integers (i.e. 4*4=16).
	win := WinExist() ; LastFound is unreliable when a timer is running.
	DllCall("GetWindowRect", "Ptr", notepadHwnd, "Ptr", Rect)  ; WinExist returns an Hwnd.
	L := NumGet(Rect, 0, "Int"), T := NumGet(Rect, 4, "Int")
	R := NumGet(Rect, 8, "Int"), B := NumGet(Rect, 12, "Int")
	MsgBox Format("Left: {1} Top: {2} Right: {3} Bottom: {4}", L, T, R, B)
	WinClose(notepadHwnd)
}

vtable(ptr, n) {
	; NumGet(ptr, "ptr") returns the address of the object's virtual function
	; table (vtable for short). The remainder of the expression retrieves
	; the address of the nth function's address from the vtable.
	return NumGet(NumGet(ptr, "ptr"), n*A_PtrSize, "ptr")
}

DllFillRect(*)
{
	Rect := Buffer(16)  ; Set capacity to hold four 4-byte integers.
	NumPut( "Int", 0                  ; left
			, "Int", 0                  ; top
			, "Int", A_ScreenWidth//2   ; right
			, "Int", A_ScreenHeight//2  ; bottom
			, Rect)
	hDC := DllCall("GetDC", "Ptr", 0, "Ptr")  ; Pass zero to get the desktop's device context.
	hBrush := DllCall("CreateSolidBrush", "UInt", 0x0000FF, "Ptr")  ; Create a red brush (0x0000FF is in BGR format).
	DllCall("FillRect", "Ptr", hDC, "Ptr", Rect, "Ptr", hBrush)  ; Fill the specified rectangle using the brush above.
	DllCall("ReleaseDC", "Ptr", 0, "Ptr", hDC)  ; Clean-up.
	DllCall("DeleteObject", "Ptr", hBrush)  ; Clean-up.
}

DllDeleteFromTaskbar(*)
{
	IID_ITaskbarList  := "{56FDF342-FD6D-11d0-958A-006097C9A090}"
	CLSID_TaskbarList := "{56FDF344-FD6D-11d0-958A-006097C9A090}"

	; Create the TaskbarList object.
	tbl := ComObject(CLSID_TaskbarList, IID_ITaskbarList)

	activeHwnd := WinExist("A")

	DllCall(vtable(tbl.ptr,3), "ptr", tbl)                     ; tbl.HrInit()
	DllCall(vtable(tbl.ptr,5), "ptr", tbl, "ptr", activeHwnd)  ; tbl.DeleteTab(activeHwnd)
	Sleep 3000
	DllCall(vtable(tbl.ptr,4), "ptr", tbl, "ptr", activeHwnd)  ; tbl.AddTab(activeHwnd)

	; tbl gets automatically garbage-collected
}

ComDeleteFromTaskbar(*)
{
	IID_ITaskbarList  := "{56FDF342-FD6D-11d0-958A-006097C9A090}"
	CLSID_TaskbarList := "{56FDF344-FD6D-11d0-958A-006097C9A090}"

	; Create the TaskbarList object.
	tbl := ComObject(CLSID_TaskbarList, IID_ITaskbarList)

	activeHwnd := WinExist("A")

	ComCall(3, tbl)                     ; tbl.HrInit()
	ComCall(5, tbl, "ptr", activeHwnd)  ; tbl.DeleteTab(activeHwnd)
	Sleep 3000
	ComCall(4, tbl, "ptr", activeHwnd)  ; tbl.AddTab(activeHwnd)

	; When finished with the object, simply replace any references with
	; some other value (or if its a local variable, just return):
	tbl := ""
}

ComRunWord(*)
{
	wd := ComObject("Word.Application")
	wd.Visible := "True"
	WinMaximize("ahk_exe winword.exe")
	doc := wd.Documents.Add()
	wd.Selection.TypeText("Hi Keysharp!")
}

handlerobj := ""

ComRunWordEventListener(*)
{
	global handlerobj := mycomhandler()
	wd := ComObject("Word.Application")
	ComObjConnect(wd, handlerobj, true)
	wd.Visible := "True"
	WinMaximize("ahk_exe winword.exe")
	doc := wd.Documents.Add()
	wd.Selection.TypeText("Keysharp should receive events from this.")
}

class mycomhandler
{
	WindowActivate(obj1, obj2, comobj)
	{
		OutputDebug("`tReceived WindowActivate event.")
		ShowDebug()
	}

	WindowDeactivate(obj1, obj2, comobj)
	{
		OutputDebug("`tReceived WindowDeactivate event.")
		ShowDebug()
	}

	NewDocument(obj1, comobj)
	{
		OutputDebug("`tReceived NewDocument event.")
		ShowDebug()
	}

	DocumentChange(comobj)
	{
		OutputDebug("`tReceived DocumentChange event.")
		ShowDebug()
	}

	WindowSize(obj1, obj2, comobj)
	{
		OutputDebug("`tReceived WindowSize event.")
		ShowDebug()
	}

	DocumentBeforeClose(obj1, obj2, comobj)
	{
		OutputDebug("`tReceived DocumentBeforeClose event.")
		ShowDebug()
	}

	Quit(comobj)
	{
		OutputDebug("`tReceived Quit event.")
		ShowDebug()
	}
}

shell := unset

ComExecNotepadShell(*)
{
	global shell

	if (shell is unset)
		shell := ComObject("WScript.Shell")

	exec := shell.Exec("Notepad.exe")
}

ComRunNotepadShell(*)
{
	global shell

	if (shell is unset)
		shell := ComObject("WScript.Shell")

	exec := shell.Run("Notepad.exe")
}

; Try a fake COM call.
ReturnString() => StrPtr("hello")

FakeComCall(*)
{
	; Create dummy vtable without a defined AddRef, Release etc
	vtbl := Buffer(4*A_PtrSize)
	NumPut("ptr", CallbackCreate(ReturnString), vtbl, 3*A_PtrSize)
	; Add the vtbl to our COM object
	dummyCOM := Buffer(A_PtrSize, 0)
	NumPut("ptr", vtbl.Ptr, dummyCOM)
	val := ComCall(3, dummyCOM.Ptr, "str")
	MsgBox(val)
}

#c::SystemCursor("Toggle")  ; Win+C hotkey to toggle the cursor on and off.

SystemCursor(cmd)  ; cmd = "Show|Hide|Toggle|Reload"
{
    static visible := true, c := Map()
    static sys_cursors := [32512, 32513, 32514, 32515, 32516, 32642
                         , 32643, 32644, 32645, 32646, 32648, 32649, 32650]
    if (cmd = "Reload" or !c.Count)  ; Reload when requested or at first call.
    {
        for i, id in sys_cursors
        {
            h_cursor  := DllCall("LoadCursor", "Ptr", 0, "Ptr", id)
            h_default := DllCall("CopyImage", "Ptr", h_cursor, "UInt", 2
                , "Int", 0, "Int", 0, "UInt", 0)
            h_blank   := DllCall("CreateCursor", "Ptr", 0, "Int", 0, "Int", 0
                , "Int", 32, "Int", 32
                , "Ptr", Buffer(32*4, 0xFF)
                , "Ptr", Buffer(32*4, 0))
            c[id] := {def: h_default, blank: h_blank}
        }
    }
    switch cmd
    {
    case "Show": visible := true
    case "Hide": visible := false
    case "Toggle": visible := !visible
    default: return
    }
    for id, handles in c
    {
        h_cursor := DllCall("CopyImage"
            , "Ptr", visible ? handles.def : handles.blank
            , "UInt", 2, "Int", 0, "Int", 0, "UInt", 0)
        DllCall("SetSystemCursor", "Ptr", h_cursor, "UInt", id)
    }
}

OnExit (*) => SystemCursor("Show")  ; Ensure the cursor is made visible when the script exits.
#endif

; ┌──────────────────┐
; │  Sound Tab       │
; └──────────────────┘
MyGui.UseGroup()
Tab.UseTab("Sound")

	#if WINDOWS
		audioMeter := SoundGetInterface("{C02216F6-8C67-4B5B-9D00-D008E73E0064}")
	#endif

txtMasterName := MyGui.Add("Text", "xc+10 y+10 w400", "Master: " . TrySoundGetName())

txtMasterVol := MyGui.Add("Text", "xp y+10 w200", "Volume: " . TrySoundGetVolume())
txtMasterMute := MyGui.Add("Text", "xp y+10 w200", "Muted: " . TrySoundGetMute())
#if WINDOWS
	txtMasterPeak := MyGui.Add("Text", "xp y+10 w200", "Peak: " . MasterPeak())
#endif
btnMasterMute := MyGui.Add("Button", "xp y+10", "Mute")
btnMasterUnmute := MyGui.Add("Button", "xp y+10", "Unmute")
btnMasterRefresh := MyGui.Add("Button", "xp y+10", "Refresh")

btnMasterMute.OnEvent("Click", "MasterMute")
MasterMute(*)
{
	TrySoundSetMute(true)
}

btnMasterUnmute.OnEvent("Click", "MasterUnmute")
MasterUnmute(*)
{
	TrySoundSetMute(false)
}

btnMasterRefresh.OnEvent("Click", "RefreshSound")
RefreshSound(*)
{
	txtMasterName.Text := "Master: " . TrySoundGetName()
	txtMasterVol.Text := "Volume: " . TrySoundGetVolume()
	txtMasterMute.Text := "Muted: " . TrySoundGetMute()
#if WINDOWS
	txtMasterPeak.Text := "Peak: " . MasterPeak()
#endif
}

txtMasterVolumeSlider := MyGui.Add("Text", "xc+10 cBlue s10", "Moving slider sets master volume")
sldMasterVolume := MyGui.Add("Slider", "xp y+10 +AltSubmit Page10 ToolTip Range0-100", 100)
sldMasterVolume.OnEvent("Change", "MasterVolumeSliderPos")

MasterVolumeSliderPos(*)
{
	val := sldMasterVolume.Value
	TrySoundSetVolume(val)
	txtMasterVol.Text := "Volume: " . TrySoundGetVolume()
}

txtAdjMasterVolumeSlider := MyGui.Add("Text", "xc+10 cBlue s10", "Moving slider adjusts master volume")
sldAdjMasterVolume := MyGui.Add("Slider", "xp y+10 +AltSubmit Page10 ToolTip Range-100-100", 100)
sldAdjMasterVolume.OnEvent("Change", "AdjustMasterVolumeSliderPos")

AdjustMasterVolumeSliderPos(*)
{
	val := sldAdjMasterVolume.Value

	if (val >= 0)
		val := "+" . val

	TrySoundSetVolume(val)
	txtMasterVol.Text := "Volume: " . TrySoundGetVolume()
}

TrySoundGetName()
{
	try
		return SoundGetName()
	catch
		return "Unavailable"
}

TrySoundGetVolume()
{
	try
		return SoundGetVolume()
	catch
		return "Unavailable"
}

TrySoundGetMute()
{
	try
		return SoundGetMute()
	catch
		return "Unavailable"
}

TrySoundSetMute(mute)
{
	try
		SoundSetMute(mute)
}

TrySoundSetVolume(volume)
{
	try
		SoundSetVolume(volume)
}

#if WINDOWS
MasterPeak()
{
	global audioMeter
	ComCall 3, audioMeter, "float*", &peak := 0
	return peak
}
#endif

beepBtn := MyGui.Add("Button", "xp y+10", "Beep")
beepBtn.OnEvent("Click", "DoBeep")

DoBeep(*)
{
	SoundBeep(1500, 1000)
}

wavBtn := MyGui.Add("Button", "xp y+10", "Play wav")
wavBtn.OnEvent("Click", "DoWav")

wavTxt := MyGui.Add("Edit", "x+10 yp+2 w400")

#if LINUX
	wavTxt.Text := "/usr/share/sounds/linuxmint-login.wav"
#elif OSX
	wavTxt.Text := "/System/Library/Sounds/Ping.aiff"
#else
	wavTxt.Text := "C:\Windows\Media\Windows Shutdown.wav"
#endif

DoWav(*)
{
	SoundPlay(wavTxt.Text, 1)
}

; ── Image tab: OCR probe alongside the image / pixel controls. ──
Tab.UseTab("Image")
imgGroup := MyGui.AddGroupBox("xc+10 yc+10 w500", "Images (Picture / ImageSearch / ScreenClip)")
MyGui.UseGroup(imgGroup)
MyGui.AddText("xc+16 yc+24 w468 h44", "Display loads monkey/icon/svg Picture controls then destroys them; ImageSearch locates the colour swatch on screen and checks the coordinates against the swatch's own screen rect; ScreenClip captures a region and shows it. Pictures render on this tab.")
imgDisplayBtn := MyGui.AddButton("xc+16 y+10 w150 h28", "Display Pictures")
imgDisplayBtn.OnEvent("Click", "LoadPic")
imgDestroyBtn := MyGui.AddButton("x+10 yp w150 h28", "Destroy Pictures")
imgDestroyBtn.OnEvent("Click", "DestroyPic")
imgSearchBtn := MyGui.AddButton("xc+16 y+10 w180 h28", "Image Search (swatch)")
imgSearchBtn.OnEvent("Click", "ImgSrch")
imgScreenClipBtn := MyGui.AddButton("x+10 yp w120 h28", "Screen Clip")
imgScreenClipBtn.OnEvent("Click", "LoadSC")
imgOverlayBtn := MyGui.AddButton("xc+16 y+10 w230 h28", "Overlay Test (corner shapes + text)")
imgOverlayBtn.OnEvent("Click", (*) => RunOverlayTest())
MyGui.AddText("xc+16 y+12 w468", "Picture control render test (killbill.png, native size). NOTE: a rendered image file is not searchable — macOS/Wayland rescale it and macOS converts its colours — so Image Search uses the colour swatch instead:")
SrchPic := MyGui.Add("Picture", "xc+16 y+6 w-1 h-1", A_WorkingDir . A_DirSeparator . "killbill.png")
imgSearchStatus := MyGui.AddText("xc+16 yc+330 w468 h40", "Image status: Not run")
gStatus["image_main"] := imgSearchStatus
MyGui.UseGroup()
Tab.UseTab("Image")
ocrGroup := MyGui.AddGroupBox("xc+540 yc+10 w540", "OCR (OCR.ks: FindString / Filter / Crop)")
MyGui.UseGroup(ocrGroup)
MyGui.AddText("xc+16 yc+24 w508 h44", "Builds a window with known text, OCRs it via OCR.FromWindow, then exercises FindString / FindStrings / Filter / Crop / WordsBoundingRect / Cluster. Requires Tesseract (auto-detected; set OCR.Engine.Library if not).")
btnOcrRun := MyGui.AddButton("xc+16 y+10 w150 h28", "Run OCR Test")
btnOcrRun.OnEvent("Click", (*) => RunOcrTest())
ocrStatus := MyGui.AddText("x+12 yp+4 w340 h24", "OCR status: Not run")
gStatus["ocr_main"] := ocrStatus
gOcrResultEdit := MyGui.AddEdit("xc+16 y+10 w508 h244 ReadOnly -Wrap")
MyGui.UseGroup()
Tab.UseTab()

; Pixel tests (PixelGetColor / PixelSearch) — sample the on-tab colour swatch, no helper window needed.
Tab.UseTab("Image")
; Placed below the OCR group: "y+10" references the previous control, which after re-selecting the Image
; tab is the last group added to it (ocrGroup). No need to measure ocrGroup's (auto) height explicitly.
pixelGroup := MyGui.AddGroupBox("xc+540 y+10 w540", "Pixel (PixelGetColor / PixelSearch)")
MyGui.UseGroup(pixelGroup)
MyGui.AddText("xc+16 yc+24 w508 h28", "Samples the colour swatch on the right (no helper window). PixelGetColor reads its centre; PixelSearch locates that colour within the swatch's screen bounds.")
btnPixelGet := MyGui.AddButton("xc+16 y+10 w130 h30", "PixelGetColor")
btnPixelGet.OnEvent("Click", (*) => RunPixelGetColorTest())
btnPixelSearch := MyGui.AddButton("x+10 yp w130 h30", "PixelSearch")
btnPixelSearch.OnEvent("Click", (*) => RunPixelSearchTest())
pixelSwatch := MyGui.Add("Text", "x+18 yp-4 w130 h38 Border BackgroundCC5533", "")
pixelStatus := MyGui.AddText("xc+16 y+12 w508 h40", "Pixel status: Not run")
gStatus["pixel_main"] := pixelStatus
MyGui.UseGroup()
Tab.UseTab()

; ── Windows tab: window capture (Ks Image.FromWindow) + WinEvent probes (guitest's window move/min/max tests move here too) ──
Tab.UseTab("Windows")
externalWinGroup := MyGui.AddGroupBox("xc+16 yc+10 w540", "Window Capture / Activate / Move")
MyGui.UseGroup(externalWinGroup)
MyGui.AddText("xc+16 yc+24 w508 h34", "Use Capture Active Window to prefill the title field, or type your own title match. Activate and Move are semi-automated and should be confirmed by the tester.")
gWindowTitleEdit := MyGui.AddEdit("xc+16 y+8 w508", "")
btnCaptureActive := MyGui.AddButton("xc+16 y+10 w150 h28", "Capture Active")
btnCaptureActive.OnEvent("Click", (*) => CaptureActiveWindow())
btnActivateTarget := MyGui.AddButton("x+10 yp w150 h28", "Activate Title")
btnActivateTarget.OnEvent("Click", (*) => ActivateExternalWindow())
btnMoveTarget := MyGui.AddButton("x+10 yp w188 h28", "Move Title +40,+40")
btnMoveTarget.OnEvent("Click", (*) => MoveExternalWindow())
btnFromPoint := MyGui.AddButton("xc+16 y+10 w220 h28", "Use Window From Mouse Point")
btnFromPoint.OnEvent("Click", (*) => CaptureWindowFromPoint())
MyGui.AddText("x+12 yp w276 h34", "Reads the window under the current mouse cursor and fills the target title.")
gWindowInfoEdit := MyGui.AddEdit("xc+16 y+10 w508 h140 ReadOnly -Wrap")
externalStatus := MyGui.AddText("xc+16 y+8 w508 h28", "External status: waiting for a target title")
gStatus["window_external"] := externalStatus
MyGui.UseGroup()
Tab.UseTab("Windows")
winEventGroup := MyGui.AddGroupBox("xc+570 yc+10 w560", "WinEvent (Ks.WinEvent) Window Event Subscriptions")
MyGui.UseGroup(winEventGroup)
MyGui.AddText("xc+16 yc+24 w528 h54", "Subscribes to Active / Exist / NotExist / Move / Minimize / Restore / TitleChange / CaretMove through Ks.WinEvent and logs them. Move and CaretMove events are counted (not logged) to avoid flooding. After starting, switch, open, close, minimize, restore, and drag windows, and type into a text field.")
btnStartWinEvent := MyGui.AddButton("xc+16 y+10 w200 h28", "Start WinEvent Probe")
btnStartWinEvent.OnEvent("Click", (*) => StartWinEventProbe())
btnStopWinEvent := MyGui.AddButton("x+10 yp w200 h28", "Stop WinEvent Probe")
btnStopWinEvent.OnEvent("Click", (*) => StopWinEventProbe())
winEventStatus := MyGui.AddText("xc+16 y+10 w528 h24", "WinEvent: not started")
gStatus["window_winevent"] := winEventStatus
MyGui.UseGroup()
Tab.UseTab()

; This-window tests (they manipulate whole windows). "Move GUI" moves THIS window only; arbitrary-title
; moves live in the Window Capture / Activate / Move group above.
Tab.UseTab("Windows")
; Span below the taller of the two (now auto-height) groups on the first row.
externalWinGroup.GetPos(&_ewX, &_ewY, &_ewW, &_ewH)
winEventGroup.GetPos(&_weX, &_weY, &_weW, &_weH)
guiSelfGroup := MyGui.AddGroupBox("xc+16 yc+" (10 + Max(_ewH, _weH) + 10) " w1114", "This-window tests (Move / Title / Notepad)")
MyGui.UseGroup(guiSelfGroup)
MoveText := MyGui.AddText("xc+16 yc+24 w380 h20", "Move this window to (100,100), then restore it (button colour tracks focus):")
MoveText.SetFont("s9 cBlue")
MoveButton := MyGui.AddButton("xc+16 y+6 w120 h26", "Move GUI")
MoveButton.OnEvent("Focus", "ChangeMoveBtnColor")
MoveButton.OnEvent("Click", "MoveGui")
MoveButtonBack := MyGui.AddButton("x+8 yp w120 h26", "Move GUI Back")
MoveButtonBack.OnEvent("Focus", "ChangeMoveBtnBackColor")
MoveButtonBack.OnEvent("Click", "MoveGuiBack")
#if WINDOWS
TitleInfo := MyGui.AddText("xc+420 yc+24 w340 h20", "Alter this window's title via SendMessage (WM_SETTEXT):")
TitleInfo.SetFont("cBlue s9")
SendBtn1 := MyGui.AddButton("xc+420 y+6 w120 h26", "Change Title")
SendBtn1.OnEvent("Click", "ChangeTitle")
SendBtn2 := MyGui.AddButton("x+8 yp w120 h26", "Restore Title")
SendBtn2.OnEvent("Click", "RestoreTitle")
PostInfo := MyGui.AddText("xc+800 yc+24 w300 h20", "Launch Notepad, PostMessage its About box, then close it:")
PostInfo.SetFont("cBlue s9")
PostBtn1 := MyGui.AddButton("xc+800 y+6 w190 h26", "Show Notepad 'About'")
PostBtn1.OnEvent("Click", "AboutNotepad")
#endif
MyGui.UseGroup()
Tab.UseTab()

ChangeMoveBtnColor(*) {
	MoveButton.SetFont("cRed")
	MoveButtonBack.SetFont("cBlack")
}

ChangeMoveBtnBackColor(*) {
	MoveButton.SetFont("cBlack")
	MoveButtonBack.SetFont("cRed")
}

#if WINDOWS
ChangeTitle(*) {
	Title := "KEYSHARP'S BRAND SPANKING NEW TITLE"
	SendMessage(0x000C, 0, Title)  ; 0X000C is WM_SETTEXT
}

RestoreTitle(*) {
	Title := "KEYSHARP TESTS"
	SendMessage(0x000C, 0, Title)  ; 0X000C is WM_SETTEXT
}

AboutNotepad(*) {
	SetTitleMatchMode(2)
	Run("Notepad.exe")
	Sleep(1000)
	PostMessage(0x0111, 65, 0, , "Untitled - Notepad")
	Sleep(2000)
	WinKill("ahk_exe Notepad.exe")
}
#endif

; ── Clipboard tab ──
Tab.UseTab("Clipboard")
clipGroup := MyGui.AddGroupBox("xc+16 yc+10 w560", "Clipboard Tests")
MyGui.UseGroup(clipGroup)
MyGui.AddText("xc+16 yc+24 w528 h34", "Text round-trip and delayed ClipWait are self-validating. Clipboard change monitoring shows whether callbacks are fired. Image copy is manual after the copy step succeeds.")
gClipboardTextEdit := MyGui.AddEdit("xc+16 y+8 w528 h92", "Clipboard probe text:`nAlpha beta gamma`nUnicode: Eesti, 日本語, emoji-free.")
btnClipboardRoundTrip := MyGui.AddButton("xc+16 y+10 w150 h28", "Text Round Trip")
btnClipboardRoundTrip.OnEvent("Click", (*) => RunClipboardTextRoundTrip())
btnClipWait := MyGui.AddButton("x+10 yp w150 h28", "Delayed ClipWait")
btnClipWait.OnEvent("Click", (*) => RunClipboardClipWaitTest())
btnClipboardImage := MyGui.AddButton("x+10 yp w148 h28", "Copy Image Asset")
btnClipboardImage.OnEvent("Click", (*) => RunClipboardImageCopy())
btnToggleMonitor := MyGui.AddButton("xc+16 y+10 w150 h28", "Toggle Change Monitor")
btnToggleMonitor.OnEvent("Click", (*) => ToggleClipboardMonitor())
; (Pass/fail for the clipboard tests is shown in the status bar.)
clipMonitorStatus := MyGui.AddText("xc+16 y+10 w528 h24", "Clipboard monitor: disabled")
gStatus["clipboard_monitor"] := clipMonitorStatus
MyGui.UseGroup()
Tab.UseTab()

Tab.UseTab("Send && Hotkey")
sendGroup := MyGui.AddGroupBox("xc+10 yc+10 w500", "Send Variants")
MyGui.UseGroup(sendGroup)
MyGui.AddText("xc+16 yc+24 w468", "Each button clears the target edit, focuses it, sends text, and validates the exact resulting text.")
gSendTarget := MyGui.AddEdit("xc+16 y+8 w468 h90 -Wrap")
btnSendV := MyGui.AddButton("xc+16 y+10 w110 h28", "Send()")
btnSendV.OnEvent("Click", (*) => RunSendVariant("Send"))
btnSendTextV := MyGui.AddButton("x+8 yp w110 h28", "SendText()")
btnSendTextV.OnEvent("Click", (*) => RunSendVariant("SendText"))
btnSendInputV := MyGui.AddButton("x+8 yp w110 h28", "SendInput()")
btnSendInputV.OnEvent("Click", (*) => RunSendVariant("SendInput"))
btnSendPlayV := MyGui.AddButton("x+8 yp w110 h28", "SendPlay()")
btnSendPlayV.OnEvent("Click", (*) => RunSendVariant("SendPlay"))
; SendEvent / ControlSend / ControlSendText — manual send tests grouped with the other Send buttons (they target gSendTarget too)
BtnSendEvent := MyGui.AddButton("xc+16 y+8 w110 h28", "SendEvent()")
BtnSendEvent.OnEvent("Click", "BtnSendEventFunc")
BtnControlSend := MyGui.AddButton("x+8 yp w110 h28", "ControlSend()")
BtnControlSend.OnEvent("Click", "BtnControlSendFunc")
BtnControlSendText := MyGui.AddButton("x+8 yp w130 h28", "ControlSendText()")
BtnControlSendText.OnEvent("Click", "BtnControlSendTextFunc")
btnSendUnicode := MyGui.AddButton("xc+16 y+10 w150 h28", "Unicode SendText")
btnSendUnicode.OnEvent("Click", (*) => RunSendScenario("SendText", "Mägi, Köln, São Paulo`n", "Unicode SendText"))
btnSendEmoji := MyGui.AddButton("x+12 yp w150 h28", "Emoji SendText")
btnSendEmoji.OnEvent("Click", (*) => RunSendScenario("SendText", "Faces: 😀 😎 🚀`n", "Emoji SendText"))
btnSendMixed := MyGui.AddButton("x+12 yp w146 h28", "Mixed Unicode")
btnSendMixed.OnEvent("Click", (*) => RunSendScenario("SendText", "Mixed: ääkkönen, 日本語, 😀`n", "Mixed Unicode SendInput"))
btnSendCaret := MyGui.AddButton("xc+16 y+10 w280 h28", "SendInput caret→start")
btnSendCaret.OnEvent("Click", (*) => RunSendCaretTest())
btnSendRaw := MyGui.AddButton("x+10 yp w160 h28", "Send {Raw}")
btnSendRaw.OnEvent("Click", (*) => RunSendRawTest())
MyGui.AddText("xc+16 y+10 w468 h30", "Extra coverage: Unicode, accented Latin text, CJK text, and emoji. Useful for keyboard-layout and surrogate-pair issues.")
; (Pass/fail for the Send tests is shown in the status bar.)
MyGui.UseGroup()
Tab.UseTab("Send && Hotkey")
hotkeyGroup := MyGui.AddGroupBox("xc+530 yc+10 w540", "Hotkey / Hotstring / InputHook")
MyGui.UseGroup(hotkeyGroup)
MyGui.AddText("xc+16 yc+24 w508 h34", "Hotkey probe: test several modifier combinations. Hotstring probe: run the 3-case matrix in the edit below. InputHook: click Start, type the expected text, then press Enter.")
MyGui.AddText("xc+16 y+6 w508 h40", "Hotstring matrix: 1) kssuite  2) ksend<Space>  3) prefixksword<Space>. These also verify documented A_EndChar behavior. Modifier mapping hint: Ctrl = ^, Alt/Option = !, Shift = +, Win/Cmd = #.")
btnResetHotkey := MyGui.AddButton("xc+16 y+10 w170 h28", "Reset Hotkey Counter")
btnResetHotkey.OnEvent("Click", (*) => ResetHotkeyProbe())
MyGui.AddText("x+12 yp w320 h28", "Press: Ctrl+Alt+1, Win/Cmd+Ctrl+9, Win/Cmd+Alt+0")
hotkeyStatus := MyGui.AddText("xc+16 y+10 w508 h32", "Hotkey status: waiting for the matrix hotkeys")
gStatus["input_hotkey"] := hotkeyStatus
gHotstringTarget := MyGui.AddEdit("xc+16 y+10 w508 h74 -Wrap")
gHotstringTarget.Value := HotstringProbeInstructions()
btnResetHotstring := MyGui.AddButton("xc+16 y+6 w170 h28", "Reset Hotstring Probe")
btnResetHotstring.OnEvent("Click", (*) => ResetHotstringProbe())
btnValidateHotstring := MyGui.AddButton("x+12 yp w170 h28", "Validate Hotstring")
btnValidateHotstring.OnEvent("Click", (*) => ValidateHotstringProbe())
hotstringStatus := MyGui.AddText("xc+16 y+6 w508 h24", "Hotstring status: waiting for the 3 probe cases")
gStatus["input_hotstring"] := hotstringStatus
MyGui.AddText("xc+16 y+12 w64 h24", "Expected:")
gInputHookExpected := MyGui.AddEdit("x+6 yp-2 w120", "abc123")
btnStartInputHook := MyGui.AddButton("x+16 yp-2 w140 h28", "Start InputHook")
btnStartInputHook.OnEvent("Click", (*) => StartInputHookProbe())
btnValidateInputHook := MyGui.AddButton("x+12 yp w140 h28", "Validate Hook")
btnValidateInputHook.OnEvent("Click", (*) => ValidateInputHookProbe())
gInputHookActual := MyGui.AddEdit("xc+16 y+6 w508 h26 ReadOnly")
inputHookStatus := MyGui.AddText("xc+16 y+6 w508 h24", "InputHook status: idle")
gStatus["input_hook"] := inputHookStatus
MyGui.UseGroup()
Tab.UseTab("Send && Hotkey")
; Sit below the (now auto-height) Send Variants group.
sendGroup.GetPos(&_sgX, &_sgY, &_sgW, &_sgH)
mouseGroup := MyGui.AddGroupBox("xc+10 yc+" (10 + _sgH + 10) " w500", "Mouse InputHook (OnMouseDown / OnMouseUp / OnMouseMove)")
MyGui.UseGroup(mouseGroup)
btnStartMouse := MyGui.AddButton("xc+16 yc+24 w104 h26", "Start Mouse")
btnStartMouse.OnEvent("Click", (*) => StartMouseHookProbe())
btnStopMouse := MyGui.AddButton("x+8 yp w70 h26", "Stop")
btnStopMouse.OnEvent("Click", (*) => StopMouseHookProbe())
btnBlockMove := MyGui.AddButton("x+8 yp w120 h26", "Block Move (2s)")
btnBlockMove.OnEvent("Click", (*) => TestBlockMoveProbe())
btnBlockMButton := MyGui.AddButton("x+8 yp w160 h26", "Toggle Block MButton")
btnBlockMButton.OnEvent("Click", (*) => ToggleBlockMButton())
mouseReadout := MyGui.AddEdit("xc+16 y+10 w468 h22 ReadOnly -Wrap", "Start, then click / wheel / move the mouse to see the last event and live counts.")
gStatus["input_mouse_readout"] := mouseReadout
mouseStatus := MyGui.AddText("xc+16 y+8 w468 h22", "Mouse hook: idle")
gStatus["input_mouse"] := mouseStatus
MyGui.UseGroup()

Tab.UseTab("Send && Hotkey")
; Sit below the (now auto-height) Hotkey / Hotstring / InputHook group.
hotkeyGroup.GetPos(&_hgX, &_hgY, &_hgW, &_hgH)
manualGroup := MyGui.AddGroupBox("xc+530 yc+" (10 + _hgH + 10) " w540", "Manual Hotkey() registration")
MyGui.UseGroup(manualGroup)
FuncBtnTwo := MyGui.AddButton("xc+16 yc+24 w150 h26", "RCtrl+RShift->AltTab")
FuncBtnTwo.OnEvent("Click", "StupidTrickTwo")
FuncBtnFive := MyGui.AddButton("x+8 yp w110 h26", "Toggle AltTab")
FuncBtnFive.OnEvent("Click", "ToggleHotkey")
FuncBtnSix := MyGui.AddButton("x+8 yp w180 h26", "RCtrl+LShift->AltTab(.INI)")
FuncBtnSix.OnEvent("Click", "GrabFromIni")
FuncBtnSeven := MyGui.AddButton("xc+16 y+8 w110 h26", "Toggle .INI")
FuncBtnSeven.OnEvent("Click", "ToggleFromIni")
MyGui.AddText("xc+16 y+10 w510", "Manual only: hold F1 = slow mouse; in Explorer select files + press F3 to list their names.")
MyGui.UseGroup()
Tab.UseTab()

; --- Shared activity log, placed directly UNDER the Tab control (computed from its actual bottom so it
;     never overlaps the tab regardless of how tall the tallest tab's content is). The log spans the tab's
;     width and "Clear Log" sits at the right end of the same row (rather than on its own row below) so the
;     whole strip stays compact. ---
Tab.UseTab()
Tab.GetPos(&_tabX, &_tabY, &_tabW, &_tabH)
; The tab sits at the GUI's left margin, so anchor the log strip to that same margin (xm) rather than the
; tab's reported X - on Linux the tab control's frame is drawn a little inside its reported bounds, so reusing
; _tabX would shove the log a dozen-or-so pixels to the right of the tab. Drop it a bit further below the tab
; (the GTK frame extends past the reported bottom) so there's a visible gap instead of touching the border.
clearLogBtnW := 110
gLogEdit := MyGui.Add("Edit", "xm y" (_tabY + _tabH + 16) " w" (_tabW - clearLogBtnW - 6) " h64 ReadOnly -Wrap")
clearLogBtn := MyGui.Add("Button", "x+6 yp w" clearLogBtnW " h28", "Clear Log")
clearLogBtn.OnEvent("Click", (*) => ClearLog())
RegisterInputProbes()
AppendLog("Manual suite ready.")
MyGui.Show("Autosize")

; ============================================================================
; Functional input / window-capture / clipboard / OCR / WinEvent probes, plus the shared activity log and
; status helpers (AppendLog, SetStatus, ClearLog, ResetStatuses). The GUI groups that drive these live on the
; Image / Send && Hotkey / Windows / Clipboard / Sound tabs above; the pixel/image/window probes use guitest's
; own killbill ImageSearch and window-move fixtures rather than a dedicated helper window.
; ============================================================================

RegisterInputProbes() {
	Hotkey("^!1", (*) => HotkeyProbe("^!1 / Ctrl+Alt+1"))
	Hotkey("#^9", (*) => HotkeyProbe("#^9 / Win-or-Cmd+Ctrl+9"))
	Hotkey("#!0", (*) => HotkeyProbe("#!0 / Win-or-Cmd+Alt+0"))
}

AppendLog(message) {
	global gLogEdit, gLogText

	timeStamp := FormatTime(, "yyyy-MM-dd HH:mm:ss")
	gLogText .= (gLogText = "" ? "" : "`r`n") "[" timeStamp "] " message
	gLogEdit.Value := gLogText
	; Setting .Value replaces the whole text and leaves the view at the top, so pin it to the newest
	; line with WM_VSCROLL/SB_BOTTOM after the edit has processed its replaced text.
	PostMessage(0x115, 7, 0, gLogEdit)
}

ClearLog() {
	global gLogText, gLogEdit

	gLogText := ""
	gLogEdit.Value := ""
	AppendLog("Log cleared.")
}

SetStatus(key, text) {
	global gStatus

	if gStatus.Has(key)
		gStatus[key].Value := text

	; Mirror a definitive verdict to the status bar: green PASS / red FAIL.
	UpdateResultStatusBar(text)
}

; Shows the latest pass/fail verdict in the status bar (like the Pickers & Sliders listbox update keeps the
; Keysharp logo). "PASS if ..." / "CHECK ..." are advisory, not verdicts, so they leave the bar unchanged.
UpdateResultStatusBar(text) {
	global MySB

	up := StrUpper(text)
	if InStr(up, "FAIL") {
		MySB.SetIcon(A_KeysharpCorePath, "Keysharp.ico")
		MySB.SetFont("Bold cRed")
		MySB.SetText("FAIL  -  " text)
	} else if (InStr(up, "PASS") && !InStr(up, "PASS IF")) {
		MySB.SetIcon(A_KeysharpCorePath, "Keysharp.ico")
		MySB.SetFont("Bold cGreen")
		MySB.SetText("PASS  -  " text)
	}
}

ResetStatuses() {
	SetStatus("input_send", "Status: Not run")
	SetStatus("input_hotkey", "Hotkey status: waiting for Ctrl+Alt+1")
	SetStatus("input_hotstring", "Hotstring status: waiting for the 3 probe cases")
	SetStatus("input_hook", "InputHook status: idle")
	SetStatus("input_mouse", "Mouse hook: idle")
	SetStatus("input_mouse_readout", "Start, then click / wheel / move the mouse to see the last event and live counts.")
	SetStatus("window_helper", "Helper status: Not run")
	SetStatus("window_external", "External status: waiting for a target title")
	SetStatus("pixel_main", "Pixel status: Not run")
	SetStatus("image_main", "Image status: Not run")
	SetStatus("clipboard_main", "Clipboard status: Not run")
	SetStatus("clipboard_monitor", "Clipboard monitor: " (gClipboardMonitorEnabled ? "enabled" : "disabled"))
	SetStatus("sound_main", "Sound status: Not run")
	SetStatus("sound_control", "Sound control status: waiting")
	SetStatus("window_winevent", "WinEvent: not started")
	AppendLog("Status text reset.")
}

; Single callback for every WinEvent subscription. The event kind is read from hook.EventType,
; so one handler covers Active/Exist/NotExist/Move/Minimize/Restore/TitleChange/CaretMove. Move and
; CaretMove fire very frequently (once per drag step / per keystroke), so they are counted and
; surfaced in the status line rather than written to the log, while every other event is logged with
; the affected window's title. Both also carry a rectangle in A_EventInfo, which the status line shows
; for CaretMove so the reported caret position can be eyeballed against where the caret actually is.
OnWinEvent(hook, hwnd, dwmsEventTime) {
	global gWinEventCount, gWinEventMoveCount, gWinEventCaretCount

	evType := hook.EventType

	if (evType = "Move") {
		gWinEventMoveCount++
		SetStatus("window_winevent", "WinEvent: " gWinEventCount " events, " gWinEventMoveCount " moves (last hwnd " Format("0x{:X}", hwnd) ")")
		return
	}

	if (evType = "CaretMove") {
		gWinEventCaretCount++
		caret := A_EventInfo
		SetStatus("window_winevent", "WinEvent: " gWinEventCount " events, " gWinEventCaretCount " caret moves (last at "
			caret.x "," caret.y " " caret.w "x" caret.h " in hwnd " Format("0x{:X}", hwnd) ")")
		return
	}

	gWinEventCount++

	; Look up the title by the *pure* window id (integer), not an "ahk_id <id>" string: the integer form
	; matches regardless of A_DetectHiddenWindows, so hidden helper windows don't raise a "window not found"
	; error. Even so, a window seen by NotExist is gone by the time the callback runs, so its title lookup is
	; skipped, and every lookup is wrapped to keep the probe from ever throwing.
	title := "<n/a>"
	if (evType != "NotExist") {
		try
			title := WinGetTitle(hwnd)
		catch
			title := "<unavailable>"
	}

	AppendLog("WinEvent " evType ": hwnd=" Format("0x{:X}", hwnd) " title=" (title = "" ? "<none>" : title))
	SetStatus("window_winevent", "WinEvent: " gWinEventCount " events, " gWinEventMoveCount " moves (last " evType ")")
}

StartWinEventProbe() {
	global gWinEventHooks, gWinEventCount, gWinEventMoveCount, gWinEventCaretCount

	StopWinEventProbe()
	gWinEventCount := 0
	gWinEventMoveCount := 0
	gWinEventCaretCount := 0

	try {
		gWinEventHooks.Push(WinEvent.Active(OnWinEvent))
		gWinEventHooks.Push(WinEvent.Exist(OnWinEvent))
		gWinEventHooks.Push(WinEvent.NotExist(OnWinEvent))
		gWinEventHooks.Push(WinEvent.Move(OnWinEvent))
		gWinEventHooks.Push(WinEvent.Minimize(OnWinEvent))
		gWinEventHooks.Push(WinEvent.Restore(OnWinEvent))
		gWinEventHooks.Push(WinEvent.TitleChange(OnWinEvent))
		gWinEventHooks.Push(WinEvent.CaretMove(OnWinEvent))
		SetStatus("window_winevent", "WinEvent: probe started — switch, open, close, and drag windows")
		AppendLog("WinEvent probe started (" gWinEventHooks.Length " subscriptions). Activate, open, close, minimize, restore, and drag windows, and type in a text field.")
	} catch as err {
		SetStatus("window_winevent", "WinEvent: BLOCKED/ERROR")
		AppendLog("WinEvent probe failed: " err.Message)
	}
}

StopWinEventProbe() {
	global gWinEventHooks

	if (gWinEventHooks.Length = 0)
		return

	for hook in gWinEventHooks {
		try hook.Stop()
	}

	gWinEventHooks := []
	SetStatus("window_winevent", "WinEvent: stopped")
	AppendLog("WinEvent probe stopped.")
}

; Self-contained visual probe for the KS.Overlay builtin (and Highlight, which rides on it): draws a
; differently-styled shape in each screen corner (filled rect + label, outlined rect + disc, ellipse +
; line, filled ellipse + big text), a mouse-following banner, and a centre Highlight frame — all
; click-through, all rendered in-memory. It then animates two oscillation cycles (each corner slides to
; the centre and back) and AUTO-STOPS, tearing every overlay down. No arguments, no user input needed.
RunOverlayTest() {
	SetStatus("image_main", "Overlay: running (auto-stops in a few seconds)...")
	AppendLog("Overlay test: corner shapes + text + Highlight; animating 2 cycles, then auto-teardown.")

	; Overlay coordinates are screen-absolute, so read the mouse in screen space too — the default
	; CoordMode is "Client" (relative to this GUI's client area), which would put the mouse-following
	; banner off by the window origin.
	CoordMode("Mouse", "Screen")

	sw := A_ScreenWidth
	sh := A_ScreenHeight
	; Screen geometry stays in the platform's native coordinate space. The KS scale converts deliberately authored
	; UI sizes into that space; backing pixels are selected by the overlay renderer.
	monitor := MonitorFromPoint(sw // 2, sh // 2)
	ui := MonitorGetScale(monitor)
	mrg := 24
	bw := 150
	bh := 110
	mrgP := Round(mrg * ui)
	bwP := Round(bw * ui)
	bhP := Round(bh * ui)
	; Gui.SetFont-style options ("sN [bold ...]"); the family name is DrawText/MeasureText's own argument.
	font10 := "s" Round(10 * ui)
	font13 := "s" Round(13 * ui)
	font16 := "s" Round(16 * ui)
	font26 := "s" Round(26 * ui)

	tl := Overlay(mrgP, mrgP, bwP, bhP)
	tl.FillRect(0, 0, bwP, bhP, "0x2A5CC8")
	tl.DrawRect(0, 0, bwP - 1, bhP - 1, "0xFFFFFF", Round(2 * ui))
	tl.DrawText("Top-Left", 12 * ui, 44 * ui, "0xFFFFFF", font13, "Sans")
	tl.Show()

	tr := Overlay(sw - mrgP - bwP, mrgP, bwP, bhP)
	tr.DrawRect(0, 0, bwP - 1, bhP - 1, "0xFF3030", Round(4 * ui))
	tr.FillEllipse(bwP // 2 - 30 * ui, bhP // 2 - 30 * ui, 60 * ui, 60 * ui, "0xFFCC00")
	trSize := tr.MeasureText("TR", font16, "Sans")
	tr.DrawText("TR", (bwP - trSize.w) / 2, (bhP - trSize.h) / 2, "0x000000", font16, "Sans")
	tr.Show()

	bl := Overlay(mrgP, sh - mrgP - bhP, bwP, bhP)
	bl.DrawEllipse(2 * ui, 2 * ui, bwP - 4 * ui, bhP - 4 * ui, "0x30E070", 3 * ui)
	bl.DrawLine(6 * ui, 6 * ui, bwP - 6 * ui, bhP - 6 * ui, "0x30E070", 2 * ui)
	bl.DrawText("BL", 14 * ui, 46 * ui, "0x30E070", font13, "Sans")
	bl.Show()

	br := Overlay(sw - mrgP - bwP, sh - mrgP - bhP, bwP, bhP)
	br.FillEllipse(0, 0, bwP, bhP, "0xC030C0")
	brSize := br.MeasureText("BR", font26, "Sans")
	br.DrawText("BR", (bwP - brSize.w) / 2, (bhP - brSize.h) / 2, "0xFFFFFF", font26, "Sans")
	br.Show()

	bannerW := 260
	bannerH := 40
	bannerWP := Round(bannerW * ui)
	bannerHP := Round(bannerH * ui)
	banner := Overlay(sw // 2 - bannerWP // 2, sh // 2 - bannerHP // 2, bannerWP, bannerHP)
	banner.FillRect(0, 0, bannerWP, bannerHP, "0x101418")
	banner.DrawRect(0, 0, bannerWP - 1, bannerHP - 1, "0x30E070", Max(1, Round(ui)))
	banner.DrawText("Overlay test — auto-stops", 12 * ui, 12 * ui, "0x30E070", font10, "Sans")
	banner.Show()

	frame := Highlight(sw // 2 - Round(120 * ui), sh // 2 - Round(100 * ui), Round(240 * ui), Round(200 * ui), "Cyan", 3)
	frame.Show()

	cx := sw // 2 - bwP // 2
	cy := sh // 2 - bhP // 2
	; Corner "home" positions (spelled out: identifiers are case-insensitive, so e.g. `trY` would collide
	; with the reserved `try` keyword).
	homeTLx := mrgP
	homeTLy := mrgP
	homeTRx := sw - mrgP - bwP
	homeTRy := mrgP
	homeBLx := mrgP
	homeBLy := sh - mrgP - bhP
	homeBRx := sw - mrgP - bwP
	homeBRy := sh - mrgP - bhP

	; Two full oscillation cycles (phase 0 → 4π), then stop. Bounded loop, so it ends on its own.
	steps := 120
	Loop steps {
		phase := (A_Index / steps) * (4 * 3.14159265)
		t := (Sin(phase) + 1) / 2
		tl.Move(Round(homeTLx + (cx - homeTLx) * t), Round(homeTLy + (cy - homeTLy) * t))
		tr.Move(Round(homeTRx + (cx - homeTRx) * t), Round(homeTRy + (cy - homeTRy) * t))
		bl.Move(Round(homeBLx + (cx - homeBLx) * t), Round(homeBLy + (cy - homeBLy) * t))
		br.Move(Round(homeBRx + (cx - homeBRx) * t), Round(homeBRy + (cy - homeBRy) * t))
		MouseGetPos(&mx, &my)
		banner.Move(mx + Round(16 * ui), my + Round(16 * ui))
		Sleep(30)
	}

	tl.Destroy()
	tr.Destroy()
	bl.Destroy()
	br.Destroy()
	banner.Destroy()
	frame.Destroy()

	SetStatus("image_main", "Overlay: PASS — shapes/text/movement shown, auto-stopped, all torn down")
	AppendLog("Overlay test complete: all overlays destroyed.")
}

; Self-contained OCR probe: builds a window with known text, OCRs it via OCR.FromWindow (PrintWindow
; capture, 2x upscale), then exercises every result helper the OCR.ks refactor touched and writes a
; report to the OCR result edit. Requires Tesseract; a load failure is reported, not thrown.
RunOcrTest() {
	global gOcrResultEdit

	SetStatus("ocr_main", "OCR status: running...")

	; Known, OCR-friendly content: black monospaced text on white.
	g := Gui("+AlwaysOnTop", "OCR Probe Window")
	g.BackColor := "White"
	g.SetFont("s28 cBlack", "Consolas")
	g.AddText("x20 y20",  "Save Document")
	g.AddText("x20 y80",  "Open File Now")
	g.AddText("x20 y140", "Save Changes")
	g.Show("w440 h220")
	Sleep(400)   ; let it paint (PrintWindow capture does not need it focused)

	try
		res := OCR.FromWindow("ahk_id " g.Hwnd, {scale: 2})
	catch as err {
		g.Destroy()
		SetStatus("ocr_main", "OCR status: BLOCKED/ERROR")
		gOcrResultEdit.Value := "OCR failed to run: " err.Message "`r`n`r`nIs Tesseract installed? Set OCR.Engine.Library to its shared library if it is not auto-detected."
		AppendLog("OCR probe failed: " err.Message)
		return
	}

	report := []
	report.Push("=== Recognized text ===")
	report.Push(res.Text)
	report.Push("Lines: " res.Lines.Length "   Words: " res.Words.Length)
	report.Push("")
	report.Push("=== Words (text @ x,y wxh  conf | BoundingRect.x) ===")
	for w in res.Words
		report.Push(Format("  '{1}' @ {2},{3} {4}x{5}  conf={6}  br.x={7}", w.Text, w.x, w.y, w.w, w.h, w.Conf, w.BoundingRect.x))

	report.Push("")
	report.Push("=== FindString('Open') ===")
	openMatch := ""
	try {
		openMatch := res.FindString("Open")
		report.Push(Format("  found '{1}' at {2},{3} ({4}x{5})", openMatch.Text, openMatch.x, openMatch.y, openMatch.w, openMatch.h))
	} catch as err
		report.Push("  FindString('Open') threw: " err.Message "  (likely an OCR misread, not a code bug)")

	report.Push("=== FindStrings('Save') (expect 2) ===")
	try {
		saves := res.FindStrings("Save")
		report.Push("  occurrences: " saves.Length)
		for i, s in saves
			report.Push(Format("    #{1}: '{2}' at {3},{4}", i, s.Text, s.x, s.y))
	} catch as err
		report.Push("  FindStrings('Save') threw: " err.Message)

	report.Push("")
	report.Push("=== Filter: words with >= 5 chars (expect Document, Changes) ===")
	try {
		long := res.Filter((wd) => StrLen(wd.Text) >= 5)
		report.Push("  text:  " StrReplace(long.Text, "`n", " | ") "   (" long.Words.Length " words)")
	} catch as err
		report.Push("  Filter threw: " err.Message)

	WinGetPos(&wx, &wy, &ww, &wh, "ahk_id " g.Hwnd)
	report.Push("")
	report.Push("=== Crop: top half of the window ===")
	try {
		top := res.Crop(wx, wy, wx + ww, wy + wh // 2)
		report.Push("  text:  " StrReplace(top.Text, "`n", " | "))
	} catch as err
		report.Push("  Crop threw: " err.Message)

	if res.Words.Length {
		br := OCR.WordsBoundingRect(res.Words*)
		report.Push("")
		report.Push("=== WordsBoundingRect (all words) ===")
		report.Push(Format("  x={1} y={2} w={3} h={4} x2={5} y2={6}", br.x, br.y, br.w, br.h, br.x2, br.y2))
		try {
			clusters := OCR.Cluster(res.Words)
			report.Push("=== Cluster -> " clusters.Length " cluster(s) ===")
			for c in clusters
				report.Push("  '" c.Text "'")
		} catch as err
			report.Push("  Cluster threw: " err.Message)
	}

	; Visual: box the located "Open" match over the live window briefly, then auto-clear. Highlighting the
	; FindString result (rather than Words[1]) keeps this robust when the capture carries extra glyphs — e.g. on
	; GNOME Wayland a Gui's client-side-decorated title bar is part of the captured image, so Words[1] can be a
	; stray title-bar/button glyph at the window corner rather than real content.
	if IsObject(openMatch)
		try openMatch.Highlight(700)

	g.Destroy()

	ok := InStr(res.Text, "Save") && InStr(res.Text, "Open") && InStr(res.Text, "Document")
	SetStatus("ocr_main", "OCR status: " (ok ? "PASS - methods ran, expected words present" : "CHECK - inspect text below"))

	output := ""
	for ln in report
		output .= ln "`r`n"
	gOcrResultEdit.Value := output
	AppendLog("OCR probe " (ok ? "passed" : "ran (CHECK)") ": " res.Lines.Length " lines, " res.Words.Length " words.")
}

PrepareSendTarget() {
	global MyGui, gSendTarget

	gSendTarget.Value := ""
	WinActivate("ahk_id " MyGui.Hwnd)
	Sleep(120)
	gSendTarget.Focus()
	Sleep(120)
}

; SendInput must move the caret to the BEGINNING of the edit and insert there: type a line, send
; caret-to-start, prepend another line, then validate the exact result.
RunSendCaretTest() {
	global gSendTarget
#if OSX
	docStart := "#{Up}"   ; Cmd+Up = document start (Home only scrolls in macOS text controls)
#else
	docStart := "^{Home}" ; Ctrl+Home = document start
#endif
	try {
		PrepareSendTarget()
		SendInput("Second line")
		Sleep(150)
		SendInput(docStart "First line`n")
		Sleep(200)
		actual := NormalizeNewlines(gSendTarget.Value)
		expected := NormalizeNewlines("First line`nSecond line")
		if (actual = expected) {
			SetStatus("input_send", "SendInput caret->start: PASS")
			AppendLog("SendInput moved the caret to the start and prepended the line as expected.")
		} else {
			SetStatus("input_send", "SendInput caret->start: FAIL")
			AppendLog("SendInput caret-to-start mismatch. Expected <" expected "> but saw <" actual ">.")
		}
	} catch as err {
		SetStatus("input_send", "SendInput caret->start: BLOCKED/ERROR")
		AppendLog("SendInput caret-to-start threw: " err.Message)
	}
}

RunSendVariant(mode) {
	global gSendTarget

	expected := "Alpha 123`nLiteral braces {Blind}{Text}`n"

	try {
		PrepareSendTarget()

		switch mode {
			case "Send":
				Send("{Text}Alpha 123`nLiteral braces {Blind}{Text}`n")
			case "SendText":
				SendText("Alpha 123`r`nLiteral braces {Blind}{Text}`r`n")
			case "SendInput":
				SendInput("{Text}Alpha 123`nLiteral braces {Blind}{Text}`n")
			case "SendPlay":
				SendPlay("{Text}Alpha 123`nLiteral braces {Blind}{Text}`n")
			default:
				throw Error("Unknown send mode: " mode)
		}

		Sleep(250)
		actual := NormalizeNewlines(gSendTarget.Value)

		if (actual = NormalizeNewlines(expected)) {
			SetStatus("input_send", mode " status: PASS")
			AppendLog(mode " produced the expected text in the suite-owned edit.")
		} else {
			SetStatus("input_send", mode " status: FAIL")
			AppendLog(mode " mismatch. Expected <" expected "> but saw <" actual ">.")
		}
	} catch as err {
		SetStatus("input_send", mode " status: BLOCKED/ERROR")
		AppendLog(mode " threw an error: " err.Message)
	}
}

RunSendScenario(mode, expected, label := "") {
	global gSendTarget

	try {
		PrepareSendTarget()

		switch mode {
			case "Send":
				Send("{Text}" expected)
			case "SendText":
#if LINUX
				SendMode "Event"
				SetKeyDelay 30, -1
#endif
				SendText(expected)
			case "SendInput":
				SendInput("{Text}" expected)
			case "SendPlay":
				SendPlay("{Text}" expected)
			default:
				throw Error("Unknown send mode: " mode)
		}

		Sleep(250)
		actual := NormalizeNewlines(gSendTarget.Value)
		expectedNorm := NormalizeNewlines(expected)
		labelText := label != "" ? label : mode

		if (actual = expectedNorm) {
			SetStatus("input_send", labelText " status: PASS")
			AppendLog(labelText " produced the expected text in the suite-owned edit.")
		} else {
			SetStatus("input_send", labelText " status: FAIL")
			AppendLog(labelText " mismatch. Expected <" expectedNorm "> but saw <" actual ">.")
		}
	} catch as err {
		labelText := label != "" ? label : mode
		SetStatus("input_send", labelText " status: BLOCKED/ERROR")
		AppendLog(labelText " threw an error: " err.Message)
	}
}

; {Raw} mode must send braces and the ^ ! + # modifier symbols as literal characters rather than
; interpreting them. (This is the Send-mode coverage that used to live in the GroupBoxes tab.)
RunSendRawTest() {
	global gSendTarget

	expected := "Raw mode keeps {braces} and ^!+# literal"
	try {
		PrepareSendTarget()
		Send("{Raw}" expected)
		Sleep(250)
		actual := gSendTarget.Value

		if (actual = expected) {
			SetStatus("input_send", "Send {Raw} status: PASS")
			AppendLog("Send {Raw} sent the braces and modifier symbols literally.")
		} else {
			SetStatus("input_send", "Send {Raw} status: FAIL")
			AppendLog("Send {Raw} mismatch. Expected <" expected "> but saw <" actual ">.")
		}
	} catch as err {
		SetStatus("input_send", "Send {Raw} status: BLOCKED/ERROR")
		AppendLog("Send {Raw} threw an error: " err.Message)
	}
}

NormalizeNewlines(text) {
	text := StrReplace(text, "`r`n", "`n")
	text := StrReplace(text, "`r", "`n")
	return text
}

HotkeyProbe(label, *) {
	global gHotkeyHitCount

	gHotkeyHitCount += 1
	SetStatus("input_hotkey", "Hotkey status: PASS via " label " (" gHotkeyHitCount " hit" (gHotkeyHitCount = 1 ? "" : "s") ")")
	AppendLog("Hotkey probe fired via " label ". Total hits: " gHotkeyHitCount ".")
}

ResetHotkeyProbe() {
	global gHotkeyHitCount

	gHotkeyHitCount := 0
	SetStatus("input_hotkey", "Hotkey status: waiting for the matrix hotkeys")
	AppendLog("Hotkey counter reset.")
}

ResetHotstringProbe() {
	global gHotstringHitCount, gHotstringTarget

	gHotstringHitCount := 0
	gHotstringTarget.Value := HotstringProbeInstructions()
	SetStatus("input_hotstring", "Hotstring status: waiting for the 3 probe cases")
	AppendLog("Hotstring probe reset.")
}

ValidateHotstringProbe() {
	global gHotstringTarget

	missing := []

	for _, probe in ["KEYSHARP-SUITE [A_EndChar=<blank>]", "ENDCHAR-OK [A_EndChar=Space]", "INSIDE-WORD-OK [A_EndChar=Space]"] {
		if !InStr(gHotstringTarget.Value, probe)
			missing.Push(probe)
	}

	if missing.Length = 0 {
		SetStatus("input_hotstring", "Hotstring status: PASS (3/3 cases)")
		AppendLog("Hotstring validation passed for kssuite, ksend<Space>, and prefixksword<Space>.")
	} else {
		SetStatus("input_hotstring", "Hotstring status: FAIL")
		AppendLog("Hotstring validation failed. Missing: " JoinProbeNames(missing) ". Current edit content: " gHotstringTarget.Value)
	}
}

HotstringProbeInstructions() {
	return "Run these in order on separate lines:`n1. kssuite`n2. ksend<Space>`n3. prefixksword<Space>`n`nExpected markers:`nKEYSHARP-SUITE [A_EndChar=<blank>]`nENDCHAR-OK [A_EndChar=Space]`nINSIDE-WORD-OK [A_EndChar=Space]"
}

JoinProbeNames(items) {
	text := ""

	for index, item in items
		text .= (index = 1 ? "" : ", ") item

	return text
}

StartInputHookProbe() {
	global gInputHookObj, gInputHookLastText, gInputHookLastReason, gInputHookActual

	try {
		gInputHookLastText := ""
		gInputHookLastReason := ""
		gInputHookActual.Value := ""
		gInputHookObj := InputHook("V")
		gInputHookObj.KeyOpt("{Enter}", "E")
		gInputHookObj.OnEnd := InputHookEnded
		gInputHookObj.Start()
		SetStatus("input_hook", "InputHook status: capturing, type the expected text then press Enter")
		AppendLog("InputHook started.")
	} catch as err {
		SetStatus("input_hook", "InputHook status: BLOCKED/ERROR")
		AppendLog("InputHook start failed: " err.Message)
	}
}

InputHookEnded(hook) {
	global gInputHookLastText, gInputHookLastReason, gInputHookActual

	gInputHookLastText := hook.Input
	gInputHookLastReason := hook.EndReason
	gInputHookActual.Value := gInputHookLastText
	SetStatus("input_hook", "InputHook status: ended with reason " gInputHookLastReason)
	AppendLog("InputHook ended. Reason=" gInputHookLastReason ", Input=" gInputHookLastText)
}

ValidateInputHookProbe() {
	global gInputHookExpected, gInputHookLastText, gInputHookLastReason

	expected := gInputHookExpected.Value

	if (gInputHookLastText = expected && gInputHookLastReason != "") {
		SetStatus("input_hook", "InputHook status: PASS")
		AppendLog("InputHook validation passed.")
	} else {
		SetStatus("input_hook", "InputHook status: FAIL")
		AppendLog("InputHook validation failed. Expected <" expected "> but saw <" gInputHookLastText "> with reason <" gInputHookLastReason ">.")
	}
}

StartMouseHookProbe() {
	global gMouseHookObj, gMouseDownCount, gMouseUpCount, gMouseMoveCount

	try {
		if (IsObject(gMouseHookObj) && gMouseHookObj.InProgress)
			gMouseHookObj.Stop()

		gMouseDownCount := 0
		gMouseUpCount := 0
		gMouseMoveCount := 0
		; "V" keeps keystrokes/clicks visible (non-suppressing) so the harness stays usable.
		gMouseHookObj := InputHook("V")
		gMouseHookObj.OnMouseDown := MouseHookDown
		gMouseHookObj.OnMouseUp := MouseHookUp
		gMouseHookObj.OnMouseMove := MouseHookMove
		gMouseHookObj.Start()
		UpdateMouseHookReadout("(waiting for mouse activity)")
		SetStatus("input_mouse", "Mouse hook: capturing. Click, wheel, and move the mouse.")
		AppendLog("Mouse InputHook started.")
	} catch Error as err {
		SetStatus("input_mouse", "Mouse hook: BLOCKED/ERROR")
		AppendLog("Mouse InputHook start failed: " err.Message)
	}
}

StopMouseHookProbe() {
	global gMouseHookObj

	if (IsObject(gMouseHookObj) && gMouseHookObj.InProgress) {
		gMouseHookObj.VisibleMouseMove := true ; Make sure movement is restored on stop.
		gMouseHookObj.Stop()
	}

	SetStatus("input_mouse", "Mouse hook: stopped")
	AppendLog("Mouse InputHook stopped.")
}

MouseHookDown(hook, button, x, y) {
	global gMouseDownCount

	gMouseDownCount++
	UpdateMouseHookReadout("Down " button " @ " x "," y)
}

MouseHookUp(hook, button, x, y) {
	global gMouseUpCount

	gMouseUpCount++
	UpdateMouseHookReadout("Up " button " @ " x "," y)
}

MouseHookMove(hook, dx, dy) {
	global gMouseMoveCount

	gMouseMoveCount++

	; Movement fires continuously; refresh the readout only periodically so the GUI stays responsive.
	if (Mod(gMouseMoveCount, 10) = 0) {
		ei := A_EventInfo
		pos := (IsObject(ei) && ei.HasProp("X")) ? "  @ " ei.X "," ei.Y : "  (no position)"
		UpdateMouseHookReadout("Move: " dx "," dy pos)
	}
}

UpdateMouseHookReadout(lastEvent) {
	global gMouseDownCount, gMouseUpCount, gMouseMoveCount

	SetStatus("input_mouse_readout", "Last: " lastEvent "   |   Down=" gMouseDownCount " Up=" gMouseUpCount " Move=" gMouseMoveCount)
}

TestBlockMoveProbe() {
	global gMouseHookObj

	if !(IsObject(gMouseHookObj) && gMouseHookObj.InProgress) {
		SetStatus("input_mouse", "Mouse hook: start it first")
		return
	}

	gMouseHookObj.VisibleMouseMove := false
	SetStatus("input_mouse", "Mouse hook: movement BLOCKED for 2s (cursor should freeze, then recover).")
	AppendLog("Mouse movement suppression engaged (VisibleMouseMove:=false) for 2s.")
	SetTimer(UnblockMoveProbe, -2000) ; Auto-revert so the tester is never locked out.
}

UnblockMoveProbe() {
	global gMouseHookObj

	if (IsObject(gMouseHookObj) && gMouseHookObj.InProgress)
		gMouseHookObj.VisibleMouseMove := true

	SetStatus("input_mouse", "Mouse hook: movement restored")
	AppendLog("Mouse movement suppression released (VisibleMouseMove:=true).")
}

ToggleBlockMButton() {
	static blocked := false
	global gMouseHookObj

	if !(IsObject(gMouseHookObj) && gMouseHookObj.InProgress) {
		SetStatus("input_mouse", "Mouse hook: start it first")
		return
	}

	blocked := !blocked
	; +S suppresses the middle button like a suppressed keystroke; +V makes it visible again.
	gMouseHookObj.KeyOpt("{MButton}", blocked ? "+S" : "+V")
	SetStatus("input_mouse", "Mouse hook: MButton " (blocked ? "SUPPRESSED (middle-click should do nothing)" : "visible"))
	AppendLog("MButton suppression " (blocked ? "enabled" : "disabled") " via KeyOpt.")
}
CaptureActiveWindow() {
	global gWindowTitleEdit, gWindowInfoEdit

	try {
		hwnd := WinExist("A")
		title := WinGetTitle("ahk_id " hwnd)
		className := WinGetClass("ahk_id " hwnd)
		WinGetPos(&x, &y, &w, &h, "ahk_id " hwnd)
		gWindowTitleEdit.Value := title
		gWindowInfoEdit.Value := "Title: " title "`r`nClass: " className "`r`nHwnd: " hwnd "`r`nPos: " x "," y "  Size: " w "x" h
		SetStatus("window_external", "External status: captured active window")
		AppendLog("Captured active window: " title " [" className "]")
	} catch as err {
		SetStatus("window_external", "External status: BLOCKED/ERROR")
		AppendLog("Capture active window failed: " err.Message)
	}
}

CaptureWindowFromPoint() {
	global gWindowTitleEdit, gWindowInfoEdit

	try {
		CoordMode "Mouse", "Screen"
		MouseGetPos(&mx, &my)
		hwnd := WinFromPoint(mx, my)

		if !hwnd
			throw Error("WinFromPoint returned no hwnd for " mx "," my ".")

		title := WinGetTitle("ahk_id " hwnd)
		className := WinGetClass("ahk_id " hwnd)
		WinGetPos(&x, &y, &w, &h, "ahk_id " hwnd)
		gWindowTitleEdit.Value := title
		gWindowInfoEdit.Value := "From point: " mx "," my "`r`nTitle: " title "`r`nClass: " className "`r`nHwnd: " hwnd "`r`nPos: " x "," y "  Size: " w "x" h
		SetStatus("window_external", "External status: captured window from mouse point")
		AppendLog("CaptureWindowFromPoint found hwnd " hwnd " with title <" title "> at mouse point " mx "," my ".")
	} catch as err {
		SetStatus("window_external", "External status: BLOCKED/ERROR")
		AppendLog("CaptureWindowFromPoint failed: " err.Message)
	}
}

ActivateExternalWindow() {
	global gWindowTitleEdit

	title := Trim(gWindowTitleEdit.Value)

	if (title = "") {
		SetStatus("window_external", "External status: enter or capture a title first")
		return
	}

	try {
		WinActivate(title)
		WinWaitActive(title, , 2)
		SetStatus("window_external", "External status: PASS if the requested window came to front")
		AppendLog("ActivateExternalWindow ran against title match <" title ">.")
	} catch as err {
		SetStatus("window_external", "External status: BLOCKED/ERROR")
		AppendLog("ActivateExternalWindow failed for <" title ">: " err.Message)
	}
}

MoveExternalWindow() {
	global gWindowTitleEdit, gWindowInfoEdit

	title := Trim(gWindowTitleEdit.Value)

	if (title = "") {
		SetStatus("window_external", "External status: enter or capture a title first")
		return
	}

	try {
		WinGetPos(&x1, &y1, &w1, &h1, title)
		WinMove(x1 + 40, y1 + 40, w1, h1, title)
		Sleep(200)
		WinGetPos(&x2, &y2, &w2, &h2, title)
		gWindowInfoEdit.Value := "Before: " x1 "," y1 "  " w1 "x" h1 "`r`nAfter:  " x2 "," y2 "  " w2 "x" h2

		; Auto-verify: the window should have moved by exactly +40,+40. Allow a couple of
		; pixels of slack for window-manager frame rounding, but still catch a missed move
		; or a move along the wrong axis.
		dx := x2 - x1, dy := y2 - y1
		tol := 2
		if (Abs(dx - 40) <= tol && Abs(dy - 40) <= tol) {
			SetStatus("window_external", "External status: PASS (moved by " dx "," dy ")")
			AppendLog("MoveExternalWindow PASS for <" title ">. Before=" x1 "," y1 " After=" x2 "," y2 " delta=" dx "," dy ".")
		} else {
			SetStatus("window_external", "External status: FAIL (expected +40,+40 but moved " dx "," dy ")")
			AppendLog("MoveExternalWindow FAIL for <" title ">. Before=" x1 "," y1 " After=" x2 "," y2 " delta=" dx "," dy " (expected 40,40).")
		}
	} catch as err {
		SetStatus("window_external", "External status: BLOCKED/ERROR")
		AppendLog("MoveExternalWindow failed for <" title ">: " err.Message)
	}
}

RunClipboardTextRoundTrip() {
	global gClipboardTextEdit

	try {
		SetTimer(PopulateClipboardTimer, 0)
		expected := gClipboardTextEdit.Value
		A_Clipboard := expected
		if !WaitForClipboardValue(expected, 2000)
			throw Error("Clipboard did not round-trip to the expected text within 2 seconds.")

		if (A_Clipboard = expected) {
			SetStatus("clipboard_main", "Clipboard status: PASS")
			AppendLog("Clipboard text round-trip passed.")
		} else {
			SetStatus("clipboard_main", "Clipboard status: FAIL")
			AppendLog("Clipboard text mismatch. Expected <" expected "> but saw <" A_Clipboard ">.")
		}
	} catch as err {
		SetStatus("clipboard_main", "Clipboard status: BLOCKED/ERROR")
		AppendLog("Clipboard text round-trip failed: " err.Message)
	}
}

RunClipboardClipWaitTest() {
	global gClipboardClipWaitRunning, gClipboardDelayedPayload

	if gClipboardClipWaitRunning {
		SetStatus("clipboard_main", "Clipboard status: waiting for the previous ClipWait run")
		AppendLog("Delayed ClipWait ignored because a prior run is still active.")
		return
	}

	gClipboardClipWaitRunning := true

	try {
		SetTimer(PopulateClipboardTimer, 0)
		gClipboardDelayedPayload := "Delayed clipboard payload [" A_TickCount "]"
		SetTimer(PopulateClipboardTimer, -500)
		if !WaitForClipboardValue(gClipboardDelayedPayload, 2000)
			throw Error("Clipboard did not reach the delayed payload within 2 seconds.")

		if (A_Clipboard = gClipboardDelayedPayload) {
			SetStatus("clipboard_main", "Clipboard status: PASS (ClipWait)")
			AppendLog("Delayed ClipWait test passed.")
		} else {
			SetStatus("clipboard_main", "Clipboard status: FAIL (ClipWait)")
			AppendLog("Delayed ClipWait test failed. Clipboard now contains <" A_Clipboard ">.")
		}
	} catch as err {
		SetStatus("clipboard_main", "Clipboard status: BLOCKED/ERROR")
		AppendLog("Delayed ClipWait test failed: " err.Message)
	} finally {
		SetTimer(PopulateClipboardTimer, 0)
		gClipboardDelayedPayload := ""
		gClipboardClipWaitRunning := false
	}
}

PopulateClipboardTimer() {
	global gClipboardDelayedPayload
	A_Clipboard := gClipboardDelayedPayload
}

WaitForClipboardValue(expected, timeoutMs, pollMs := 50) {
	deadline := A_TickCount + timeoutMs

	while (A_TickCount < deadline) {
		if (A_Clipboard = expected)
			return true

		Sleep(Min(pollMs, Max(1, deadline - A_TickCount)))
	}

	return A_Clipboard = expected
}

RunClipboardImageCopy() {
	global gPixelAssetPath

	if !FileExist(gPixelAssetPath) {
		SetStatus("clipboard_main", "Clipboard status: BLOCKED - killbill.png not found")
		AppendLog("Clipboard image copy skipped because the fixture does not exist: " gPixelAssetPath)
		return
	}

	try {
		CopyImageToClipboard(gPixelAssetPath)
		ClipWait(2, true)
		SetStatus("clipboard_main", "Clipboard status: PASS if you can now paste the image into another app")
		AppendLog("Image copied to clipboard from " gPixelAssetPath ".")
	} catch as err {
		SetStatus("clipboard_main", "Clipboard status: BLOCKED/ERROR")
		AppendLog("Clipboard image copy failed: " err.Message)
	}
}

ToggleClipboardMonitor() {
	global gClipboardMonitorEnabled

	try {
		if gClipboardMonitorEnabled {
			OnClipboardChange(ClipboardChanged, 0)
			gClipboardMonitorEnabled := false
			SetStatus("clipboard_monitor", "Clipboard monitor: disabled")
			AppendLog("Clipboard change monitor disabled.")
		} else {
			OnClipboardChange(ClipboardChanged, 1)
			gClipboardMonitorEnabled := true
			SetStatus("clipboard_monitor", "Clipboard monitor: enabled (changes seen: " gClipboardChangeCount ")")
			AppendLog("Clipboard change monitor enabled.")
		}
	} catch as err {
		SetStatus("clipboard_monitor", "Clipboard monitor: BLOCKED/ERROR")
		AppendLog("Toggling clipboard monitor failed: " err.Message)
	}
}

ClipboardChanged(*) {
	global gClipboardChangeCount, gClipboardMonitorEnabled

	gClipboardChangeCount += 1
	if gClipboardMonitorEnabled
		SetStatus("clipboard_monitor", "Clipboard monitor: enabled (changes seen: " gClipboardChangeCount ")")
	AppendLog("Clipboard change callback fired. Count=" gClipboardChangeCount ".")
}

HotstringProbe(trigger, output) {
	global gHotstringHitCount

	gHotstringHitCount += 1
	SetStatus("input_hotstring", "Hotstring status: triggered via " trigger " (" gHotstringHitCount " hit" (gHotstringHitCount = 1 ? "" : "s") ")")
	AppendLog("Hotstring probe fired via " trigger ".")
	SendText(output " [A_EndChar=" DescribeEndChar(A_EndChar) "]")
}

DescribeEndChar(endChar) {
	if (endChar = "")
		return "<blank>"

	if (endChar = " ")
		return "Space"

	if (endChar = "`t")
		return "Tab"

	if (endChar = "`n" || endChar = "`r")
		return "Enter"

	return endChar
}

:*:kssuite::
{
	HotstringProbe("kssuite", "KEYSHARP-SUITE")
}

::ksend::
{
	HotstringProbe("ksend", "ENDCHAR-OK")
}

:?:ksword::
{
	HotstringProbe("ksword", "INSIDE-WORD-OK")
}
