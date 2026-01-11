/*
    AT-SPI accessibility library for AHK v2 / Keysharp (Linux only!).

    Purpose:
        Wraps libatspi to expose accessibility trees and UI automation helpers, similar to UIA or Acc.

    Requirements:
        - AT-SPI accessibility must be enabled in the desktop environment (eg screen reader turned on: 
            note that after turning it on it might be needed to log out and back in again).
        - libatspi, libglib-2.0, and libgobject must be available on the system.
        - The AT-SPI bus (org.a11y.Bus) must be running (usually is if accessibility is enabled).

    Overview:
        Entry points (AtSpi.*):
            GetRootElement(), ElementFromPoint(x, y), ElementFromHandle(WinTitle), ClearAllHighlights(), Viewer().

        Element properties (AtSpi.Accessible):
            Name, Description, Role/RoleId/RoleName, States, Attributes, Interfaces, Parent, Children,
            Location, HasFocus, ProcessId, Value/Minimum/Maximum/Text, etc.

        Element methods:
            FindElement/FindElements/WaitElement, Dump/DumpAll, Highlight/Click, Focus/ScrollTo,
            EditableText methods, and Text interface helpers.

        Viewer:
            Running this file directly opens AtSpi.Viewer for inspection.
*/

#DllLoad libatspi
#DllLoad libglib-2.0.so.0
#DllLoad libgobject-2.0.so.0

if (!A_IsCompiled and A_LineFile=A_ScriptFullPath)
    AtSpi.Viewer()

class AtSpi {
    class Enumeration {
        ; This enables getting property names from values using the array style
        __Item[param] {
            get {
                local k, v
                if !this.HasOwnProp("__CachedValues") {
                    this.__CachedValues := Map()
                    for k, v in this.OwnProps()
                        this.__CachedValues[v] := k
                }
                if this.__CachedValues.Has(param)
                    return this.__CachedValues[param]
                throw UnsetItemError("Property item `"" param "`" not found!", -2)
            }
        }
    }

    static LibAtSpi  := "libatspi"
    static LibGlib   := "libglib-2.0.so.0"
    static LibGObj   := "libgobject-2.0.so.0"
    static SymDelim  := "/"
    /**
     * Maximum depth for recursive viewer/tree operations.
     */
    static MaxRecurseDepth := 0xFFFFFFFF

    ; --- enums from atspi-constants.h ---
    static LocaleType := {
        Messages:0,
        Collate:1,
        CType:2,
        Monetary:3,
        Numeric:4,
        Time:5,
        base:this.Enumeration.Prototype
    }

    static CoordType := {
        Screen:0,
        Window:1,
        Parent:2,
        base:this.Enumeration.Prototype
    }

    static CollectionSortOrder := {
        Invalid:0,
        Canonical:1,
        Flow:2,
        Tab:3,
        ReverseCanonical:4,
        ReverseFlow:5,
        ReverseTab:6,
        LastDefined:7,
        base:this.Enumeration.Prototype
    }

    static CollectionMatchType := {
        Invalid:0,
        All:1,
        Any:2,
        None:3,
        Empty:4,
        LastDefined:5,
        base:this.Enumeration.Prototype
    }

    static CollectionTreeTraversalType := {
        RestrictChildren:0,
        RestrictSibling:1,
        InOrder:2,
        LastDefined:3,
        base:this.Enumeration.Prototype
    }

    static ComponentLayer := {
        Invalid:0,
        Background:1,
        Canvas:2,
        Widget:3,
        Mdi:4,
        Popup:5,
        Overlay:6,
        Window:7,
        LastDefined:8,
        base:this.Enumeration.Prototype
    }

    static TextBoundaryType := {
        Char:0,
        WordStart:1,
        WordEnd:2,
        SentenceStart:3,
        SentenceEnd:4,
        LineStart:5,
        LineEnd:6,
        base:this.Enumeration.Prototype
    }

    static TextGranularity := {
        Char:0,
        Word:1,
        Sentence:2,
        Line:3,
        Paragraph:4,
        base:this.Enumeration.Prototype
    }

    static TextClipType := {
        None:0,
        Min:1,
        Max:2,
        Both:3,
        base:this.Enumeration.Prototype
    }

    static StateType := {
        Invalid:0,
        Active:1,
        Armed:2,
        Busy:3,
        Checked:4,
        Collapsed:5,
        Defunct:6,
        Editable:7,
        Enabled:8,
        Expandable:9,
        Expanded:10,
        Focusable:11,
        Focused:12,
        HasTooltip:13,
        Horizontal:14,
        Iconified:15,
        Modal:16,
        MultiLine:17,
        MultiSelectable:18,
        Opaque:19,
        Pressed:20,
        Resizable:21,
        Selectable:22,
        Selected:23,
        Sensitive:24,
        Showing:25,
        SingleLine:26,
        Stale:27,
        Transient:28,
        Vertical:29,
        Visible:30,
        ManagesDescendants:31,
        Indeterminate:32,
        Required:33,
        Truncated:34,
        Animated:35,
        InvalidEntry:36,
        SupportsAutocomplete:37,
        SelectableText:38,
        IsDefault:39,
        Visited:40,
        Checkable:41,
        HasPopup:42,
        ReadOnly:43,
        LastDefined:44,
        base:this.Enumeration.Prototype
    }

    static KeyEventType := {
        KeyPressed:0,
        KeyReleased:1,
        base:this.Enumeration.Prototype
    }

    static EventType := {
        KeyPressed:0,
        KeyReleased:1,
        ButtonPressed:2,
        ButtonReleased:3,
        base:this.Enumeration.Prototype
    }

    static KeySynthType := {
        Press:0,
        Release:1,
        PressRelease:2,
        Sym:3,
        String:4,
        LockModifiers:5,
        UnlockModifiers:6,
        base:this.Enumeration.Prototype
    }

    static ModifierType := {
        Shift:0,
        ShiftLock:1,
        Control:2,
        Alt:3,
        Meta:4,
        Meta2:5,
        Meta3:6,
        NumLock:14,
        base:this.Enumeration.Prototype
    }

    static RelationType := {
        Null:0,
        LabelFor:1,
        LabelledBy:2,
        ControllerFor:3,
        ControlledBy:4,
        MemberOf:5,
        TooltipFor:6,
        NodeChildOf:7,
        NodeParentOf:8,
        Extended:9,
        FlowsTo:10,
        FlowsFrom:11,
        SubwindowOf:12,
        Embeds:13,
        EmbeddedBy:14,
        PopupFor:15,
        ParentWindowOf:16,
        DescriptionFor:17,
        DescribedBy:18,
        Details:19,
        DetailsFor:20,
        ErrorMessage:21,
        ErrorFor:22,
        LastDefined:23,
        base:this.Enumeration.Prototype
    }

    static Role := {
        Invalid:0,
        AcceleratorLabel:1,
        Alert:2,
        Animation:3,
        Arrow:4,
        Calendar:5,
        Canvas:6,
        CheckBox:7,
        CheckMenuItem:8,
        ColorChooser:9,
        ColumnHeader:10,
        ComboBox:11,
        DateEditor:12,
        DesktopIcon:13,
        DesktopFrame:14,
        Dial:15,
        Dialog:16,
        DirectoryPane:17,
        DrawingArea:18,
        FileChooser:19,
        Filler:20,
        FocusTraversable:21,
        FontChooser:22,
        Frame:23,
        GlassPane:24,
        HtmlContainer:25,
        Icon:26,
        Image:27,
        InternalFrame:28,
        Label:29,
        LayeredPane:30,
        List:31,
        ListItem:32,
        Menu:33,
        MenuBar:34,
        MenuItem:35,
        OptionPane:36,
        PageTab:37,
        PageTabList:38,
        Panel:39,
        PasswordText:40,
        PopupMenu:41,
        ProgressBar:42,
        Button:43,
        RadioButton:44,
        RadioMenuItem:45,
        RootPane:46,
        RowHeader:47,
        ScrollBar:48,
        ScrollPane:49,
        Separator:50,
        Slider:51,
        SpinButton:52,
        SplitPane:53,
        StatusBar:54,
        Table:55,
        TableCell:56,
        TableColumnHeader:57,
        TableRowHeader:58,
        TearoffMenuItem:59,
        Terminal:60,
        Text:61,
        ToggleButton:62,
        ToolBar:63,
        ToolTip:64,
        Tree:65,
        TreeTable:66,
        Unknown:67,
        Viewport:68,
        Window:69,
        Extended:70,
        Header:71,
        Footer:72,
        Paragraph:73,
        Ruler:74,
        Application:75,
        Autocomplete:76,
        Editbar:77,
        Embedded:78,
        Entry:79,
        Chart:80,
        Caption:81,
        DocumentFrame:82,
        Heading:83,
        Page:84,
        Section:85,
        RedundantObject:86,
        Form:87,
        Link:88,
        InputMethodWindow:89,
        TableRow:90,
        TreeItem:91,
        DocumentSpreadsheet:92,
        DocumentPresentation:93,
        DocumentText:94,
        DocumentWeb:95,
        DocumentEmail:96,
        Comment:97,
        ListBox:98,
        Grouping:99,
        ImageMap:100,
        Notification:101,
        InfoBar:102,
        LevelBar:103,
        TitleBar:104,
        BlockQuote:105,
        Audio:106,
        Video:107,
        Definition:108,
        Article:109,
        Landmark:110,
        Log:111,
        Marquee:112,
        Math:113,
        Rating:114,
        Timer:115,
        Static:116,
        MathFraction:117,
        MathRoot:118,
        Subscript:119,
        Superscript:120,
        DescriptionList:121,
        DescriptionTerm:122,
        DescriptionValue:123,
        Footnote:124,
        ContentDeletion:125,
        ContentInsertion:126,
        Mark:127,
        Suggestion:128,
        PushButtonMenu:129,
        Switch:130,
        LastDefined:131,
        PushButton:43,
        base:this.Enumeration.Prototype
    }

    static Cache := {
        None:0,
        Parent:1 << 0,
        Children:1 << 1,
        Name:1 << 2,
        Description:1 << 3,
        States:1 << 4,
        Role:1 << 5,
        Interfaces:1 << 6,
        Attributes:1 << 7,
        All:0x3fffffff,
        Default:1 << 0 | 1 << 1 | 1 << 2 | 1 << 3 | 1 << 4 | 1 << 5 | 1 << 6,
        Undefined:0x40000000,
        base:this.Enumeration.Prototype
    }

    static ScrollType := {
        TopLeft:0,
        BottomRight:1,
        TopEdge:2,
        BottomEdge:3,
        LeftEdge:4,
        RightEdge:5,
        Anywhere:6,
        base:this.Enumeration.Prototype
    }

    static Live := {
        None:0,
        Polite:1,
        Assertive:2,
        base:this.Enumeration.Prototype
    }

    ; MatchMode constants used in condition objects
    static MatchMode := {
        StartsWith:1,
        Substring:2,
        Exact:3,
        RegEx:"Regex",
        base:this.Enumeration.Prototype
    }

    static TreeTraversalOptions := {
        Default:0,
        PostOrder:1,
        LastToFirst:2,
        PostOrderLastToFirst:3,
        base:this.Enumeration.Prototype
    }

    ; Used wherever the scope variable is needed (eg Dump, DumpAll)
    static TreeScope := {
        Element:1,
        Children:2,
        Family:3,
        Descendants:4,
        Subtree:7,
        base:this.Enumeration.Prototype
    }

    static __HighlightGuis := Map()

    static _inited := false
    static __Sym(lib, sym) => lib . "/" . sym

    /**
     * Initializes the AT-SPI library.
     * @returns {Boolean}
     */
    static Init() {
        if this._inited
            return true

        rc := DllCall(this.__Sym(this.LibAtSpi, "atspi_init"), "Int")
        if (rc != 0 && rc != 1)
            throw Error("atspi_init failed, rc=" rc)

        this._inited := true
        return true
    }

    ; --- memory helpers ---
    static __StrAndFree(pStr) {
        if !pStr
            return ""
        s := StrGet(pStr, "UTF-8")
        DllCall(this.__Sym(this.LibGlib, "g_free"), "Ptr", pStr)
        return s
    }

    static __FreeGError(pErr) {
        if pErr
            DllCall(this.__Sym(this.LibGlib, "g_error_free"), "Ptr", pErr)
    }

    static __Unref(pObj) {
        if pObj
            DllCall(this.__Sym(this.LibGObj, "g_object_unref"), "Ptr", pObj)
    }

    /**
     * Removes all highlights created by Accessible.Highlight().
     */
    static ClearAllHighlights() {
        for _, p in AtSpi.__HighlightGuis {
            for __, r in p
                r.Destroy()
        }
        AtSpi.__HighlightGuis := Map()
    }

    static __ReadGArrayStrings(pArray) {
        if !pArray
            return []
        dataPtr := NumGet(pArray, 0, "Ptr")
        len := NumGet(pArray, A_PtrSize, "UInt")
        out := []
        Loop len {
            pStr := NumGet(dataPtr + (A_Index - 1) * A_PtrSize, 0, "Ptr")
            out.Push(pStr ? StrGet(pStr, "UTF-8") : "")
            if pStr
                DllCall(this.__Sym(this.LibGlib, "g_free"), "Ptr", pStr)
        }
        DllCall(this.__Sym(this.LibGlib, "g_array_free"), "Ptr", pArray, "Int", 1, "Ptr")
        return out
    }

    static __ReadGArrayInts(pArray) {
        if !pArray
            return []
        dataPtr := NumGet(pArray, 0, "Ptr")
        len := NumGet(pArray, A_PtrSize, "UInt")
        out := []
        Loop len
            out.Push(NumGet(dataPtr + (A_Index - 1) * 4, 0, "Int"))
        DllCall(this.__Sym(this.LibGlib, "g_array_free"), "Ptr", pArray, "Int", 1, "Ptr")
        return out
    }

    static __GValueSize := (A_PtrSize = 8 ? 24 : 16)
    static __GTypeDouble := 0

    static __NewGValueDouble(value := unset, setValue := false) {
        if !this.__GTypeDouble
            this.__GTypeDouble := DllCall(this.__Sym(this.LibGObj, "g_type_from_name"), "AStr", "gdouble", "Ptr")
        if !this.__GTypeDouble
            throw Error("Failed to resolve GType for gdouble")
        gVal := Buffer(this.__GValueSize, 0)
        DllCall(this.__Sym(this.LibGObj, "g_value_init"), "Ptr", gVal.Ptr, "Ptr", this.__GTypeDouble, "Ptr")
        if setValue
            DllCall(this.__Sym(this.LibGObj, "g_value_set_double"), "Ptr", gVal.Ptr, "Double", value, "Ptr")
        return gVal
    }

    static __ExtractNamedParameters(obj, params*) {
        local i := 0
        if !IsObject(obj) || Type(obj) != "Object"
            return 0
        Loop params.Length // 2 {
            name := params[++i], value := params[++i]
            if obj.HasProp(name)
                %value% := obj.%name%, obj.DeleteProp(name)
        }
        return 1
    }

    /**
     * Returns the desktop accessible for the given index (1-based).
     * @param index Desktop index (1-based).
     * @returns {AtSpi.Accessible|0}
     */
    static GetRootElement(index := 1) {
        this.Init()
        p := DllCall(this.__Sym(this.LibAtSpi, "atspi_get_desktop"), "Int", index - 1, "Ptr")
        return p ? AtSpi.Accessible(p) : 0
    }

    /**
     * Finds an accessible at the given screen coordinates, with a handle-based fallback.
     * @param x Screen X coordinate.
     * @param y Screen Y coordinate.
     * @returns {AtSpi.Accessible|0}
     */
    static ElementFromPoint(x?, y?) {
        this.Init()

        if !(IsSet(x) && IsSet(y)) {
            prevCoordMode := A_CoordModeMouse
            CoordMode "Mouse", "Screen"
            MouseGetPos(&mx, &my)
            CoordMode "Mouse", prevCoordMode
            if !IsSet(x)
                x := mx
            if !IsSet(y)
                y := my
        }

        desktop := this.GetRootElement()
        if desktop {
            pComp := DllCall(this.__Sym(this.LibAtSpi, "atspi_accessible_get_component_iface")
                           , "Ptr", desktop.Ptr
                           , "Ptr")

            if pComp {
                err := 0
                pHit := DllCall(this.__Sym(this.LibAtSpi, "atspi_component_get_accessible_at_point")
                              , "Ptr", pComp
                              , "Int", x
                              , "Int", y
                              , "Int", this.CoordType.Screen
                              , "Ptr*", &err
                              , "Ptr")

                this.__Unref(pComp)

                if err {
                    this.__FreeGError(err)
                } else if pHit {
                    return AtSpi.Accessible(pHit)
                }
            }
        }

        hWnd := WinFromPoint(x, y)
        if !hWnd
            return 0
        root := this.ElementFromHandle(hWnd)
        if !root
            return 0

        return this.__DeepestAccessibleAtPoint(root, x, y)
    }

    static __DeepestAccessibleAtPoint(root, x, y) {
        if !root
            return 0
        local stack, el, best := 0
        stack := [root]
        while stack.Length {
            el := stack.Pop()
            if this.__AccessibleContainsPoint(el, x, y) {
                best := el
                try count := el.ChildCount
                catch
                    continue
                Loop count {
                    try child := el.GetNthChild(A_Index)
                    catch
                        continue
                    stack.Push(child)
                }
            }
        }
        return best
    }

    static __AccessibleContainsPoint(el, x, y) {
        try loc := el.Location
        catch
            return false
        return x >= loc.x && y >= loc.y && x <= (loc.x + loc.w) && y <= (loc.y + loc.h)
    }

    /**
     * Finds an accessible matching a window handle by PID/title/geometry heuristics.
     * @param WinTitle Window title or hWnd
     * @returns {AtSpi.Accessible|0}
     */
    static ElementFromHandle(WinTitle:="") {
        this.Init()

        hWnd := IsInteger(WinTitle) ? WinTitle : WinExist(WinTitle)
        WinGetPos(&wx, &wy, &ww, &wh, hWnd)
        wTitle := WinGetTitle(hWnd)
        wPid   := WinGetPID(hWnd)

        desktop := this.GetRootElement()
        if !desktop
            return 0

        return this.__FindBestInDesktop(desktop, wTitle, wx, wy, ww, wh, wPid)
    }

    static __FindBestInDesktop(desktop, wTitle, wx, wy, ww, wh, pidFilter) {
        best := 0
        bestScore := -1

        try dCount := desktop.ChildCount
        catch
            return 0

        Loop dCount {
            app := 0
            try app := desktop.GetNthChild(A_Index)
            catch
                break

            if pidFilter {
                try {
                    if (app.ProcessId != pidFilter)
                        continue
                } catch {
                    continue
                }
            }

            try aCount := app.ChildCount
            catch
                continue

            Loop aCount {
                win := 0
                try win := app.GetNthChild(A_Index)
                catch
                    break

                s := this.__ScoreWindowCandidate(win, wTitle, wx, wy, ww, wh)
                if (s > bestScore) {
                    best := win
                    bestScore := s
                }
            }
        }

        return best
    }

    static __ScoreWindowCandidate(win, wTitle, wx, wy, ww, wh) {
        ; Must have extents for geometry matching
        try r := win.Location
        catch
            return -1

        score := 0

        ; Title score (Name vs WinGetTitle)
        name := ""
        try name := win.Name
        catch 

        if (wTitle != "" && name != "") {
            nl := StrLower(name), tl := StrLower(wTitle)
            if (nl = tl)
                score += 200
            else if (InStr(nl, tl) || InStr(tl, nl))
                score += 120
        }

        ; Role hint (light)
        role := ""
        try role := StrLower(win.Role)
        if (role = "frame" || role = "dialog" || role = "window")
            score += 30

        ; Geometry closeness
        dx := Abs(r.x - wx), dy := Abs(r.y - wy), dw := Abs(r.w - ww), dh := Abs(r.h - wh)

        tol := 8
        if (dx <= tol && dy <= tol && dw <= tol*2 && dh <= tol*2)
            score += 400

        score += Max(0, 300 - (dx + dy))
        score += Max(0, 200 - (dw + dh))

        cx1 := wx + (ww // 2), cy1 := wy + (wh // 2)
        cx2 := r.x + (r.w // 2), cy2 := r.y + (r.h // 2)
        cdist := Abs(cx1 - cx2) + Abs(cy1 - cy2)
        score += Max(0, 300 - cdist)

        return score
    }

    class Accessible {
        __ptr := 0
        __New(pAccessible) {
            if !pAccessible
                throw Error("Null AtspiAccessible pointer")
            this.__ptr := pAccessible
        }
        __Delete() => AtSpi.__Unref(this.__ptr)

        /**
         * Enables array-like use of AtSpi accessibles to access child elements.
         * If value is an integer then the nth corresponding child will be returned.
         * A negative integer returns the nth last child
         *     Eg. Element[2] ==> returns second child of Element
         *         Element[-1] ==> returns the last child of Element
         * If value is a string, then it will be parsed as a comma-separated path of integers or
         *     an AtSpi Role token with optional index (eg "Button3,2").
         * If value is an object, then it will be used in a FindElement call with scope set to Children.
         *     Eg. Element[{RoleName:"Button"}] will return the first child with RoleName Button.
         * @returns {AtSpi.Accessible}
         */
        __Item[params*] {
            get {
                local el, _, param, i, arr, found, path, m
                el := this
                for _, param in params {
                    if IsObject(param) {
                        try el := el.FindElement(param, 2)
                        catch
                            el := ""
                    } else if param is Integer {
                        try {
                            arr := el.Children
                            el := arr[param < 0 ? arr.Length+param+1 : param]
                        } catch
                            el := ""
                    } else if param is String {
                        maybeEl := ""
                        for path in StrSplit(param, "|") {
                            try {
                                if InStr(path, ".") || InStr(path, ",") {
                                    arr := StrSplit(StrReplace(path, ".", ","), ",")
                                    for i, int in arr
                                        arr[i] := IsInteger(int) ? Integer(int) : int
                                    maybeEl := el[arr*]
                                } else if RegexMatch(path, "i)([a-zA-Z]+) *(\d+)?", &m:="") && AtSpi.Role.HasOwnProp(m[1]) {
                                    try maybeEl := el.FindElement({RoleName:m[1], i:(m.Count > 1 ? m[2] : 1)}, 2)
                                    catch TargetError
                                        maybeEl := ""
                                }
                                else
                                    continue
                                break ; no errors encountered means that match was found
                            }
                        }
                        el := maybeEl
                    } else
                        throw TypeError("Invalid item type at index " _, -1)
                    if !el
                        throw IndexError("Invalid index/condition at index " _, -1)
                }
                return el
            }
        }

        /**
         * Raw AtspiAccessible pointer.
         * @returns {Ptr}
         */
        Ptr => this.__ptr

        ; --- General accessible properties ---
        /**
         * Accessible name.
         * @returns {String}
         */
        Name {
            get {
                err := 0
                p := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_name")
                           , "Ptr", this.__ptr
                           , "Ptr*", &err
                           , "Ptr")
                if err {
                    AtSpi.__FreeGError(err)
                    return ""
                }
                return AtSpi.__StrAndFree(p)
            }
        }

        /**
         * Accessible description.
         * @returns {String}
         */
        Description {
            get {
                err := 0
                p := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_description")
                           , "Ptr", this.__ptr
                           , "Ptr*", &err
                           , "Ptr")
                if err {
                    AtSpi.__FreeGError(err)
                    return ""
                }
                return AtSpi.__StrAndFree(p)
            }
        }

        ; --- Tree navigation ---
        /**
         * Parent accessible, or 0 if none.
         * @returns {AtSpi.Accessible|0}
         */
        Parent {
            get {
                err := 0
                p := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_parent")
                           , "Ptr", this.__ptr
                           , "Ptr*", &err
                           , "Ptr")
                if err {
                    AtSpi.__FreeGError(err)
                    return 0
                }
                return p ? AtSpi.Accessible(p) : 0
            }
        }

        /**
         * 1-based index in parent, or -1 if none.
         * @returns {Integer}
         */
        IndexInParent {
            get {
                err := 0
                idx := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_index_in_parent")
                             , "Ptr", this.__ptr
                             , "Ptr*", &err
                             , "Int")
                if err {
                    AtSpi.__FreeGError(err)
                    return -1
                }
                return idx < 0 ? idx : idx + 1
            }
        }

        /**
         * Number of child accessibles.
         * @returns {Integer}
         */
        ChildCount {
            get {
                err := 0
                cnt := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_child_count")
                             , "Ptr", this.__ptr
                             , "Ptr*", &err
                             , "Int")
                if err {
                    AtSpi.__FreeGError(err)
                    throw Error("Failed to get child count")
                }
                return cnt
            }
        }

        /**
         * Returns an array of child accessibles (1-based).
         * @returns {Array}
         */
        Children {
            get {
                count := this.ChildCount
                children := []
                Loop count
                    children.Push(this.GetNthChild(A_Index))
                return children
            }
        }

        /**
         * Gets the nth child (1-based).
         * @param index 1-based child index.
         * @returns {AtSpi.Accessible}
         */
        GetNthChild(index) {
            err := 0
            pChild := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_child_at_index")
                            , "Ptr", this.__ptr
                            , "Int", index - 1
                            , "Ptr*", &err
                            , "Ptr")
            if err {
                AtSpi.__FreeGError(err)
                throw Error("Failed to get child at index " index)
            }
            if !pChild
                throw Error("Child not found at index " index)
            return AtSpi.Accessible(pChild)
        }

        /**
         * Role name string from libatspi.
         * @returns {String}
         */
        Role {
            get {
                err := 0
                p := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_role_name")
                           , "Ptr", this.__ptr
                           , "Ptr*", &err
                           , "Ptr")
                if err {
                    AtSpi.__FreeGError(err)
                    return ""
                }
                return AtSpi.__StrAndFree(p)
            }
        }

        /**
         * Localized role name string from libatspi.
         * @returns {String}
         */
        LocalizedRoleName {
            get {
                err := 0
                p := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_localized_role_name")
                           , "Ptr", this.__ptr
                           , "Ptr*", &err
                           , "Ptr")
                if err {
                    AtSpi.__FreeGError(err)
                    return ""
                }
                return AtSpi.__StrAndFree(p)
            }
        }

        /**
         * Numeric role id (AtSpi.Role).
         * @returns {Integer}
         */
        RoleId {
            get {
                err := 0
                roleId := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_role")
                                , "Ptr", this.__ptr
                                , "Ptr*", &err
                                , "Int")
                if err {
                    AtSpi.__FreeGError(err)
                    return -1
                }
                return roleId
            }
        }

        /**
         * Role name resolved via AtSpi.Role enumeration.
         * @returns {String}
         */
        RoleName {
            get {
                id := this.RoleId
                if id < 0
                    return ""
                return AtSpi.Role[id]
            }
        }

        /**
         * State ids from AtSpi.StateType.
         * @returns {Array}
         */
        StateIds {
            get {
                pSet := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_state_set")
                              , "Ptr", this.__ptr
                              , "Ptr")
                if !pSet
                    return []
                pArr := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_state_set_get_states")
                              , "Ptr", pSet
                              , "Ptr")
                AtSpi.__Unref(pSet)
                return AtSpi.__ReadGArrayInts(pArr)
            }
        }

        /**
         * Alias for StateIds.
         * @returns {Array}
         */
        StateSet => this.StateIds

        /**
         * State names resolved via AtSpi.StateType.
         * @returns {Array}
         */
        States {
            get {
                names := []
                for _, id in this.StateIds {
                    try names.Push(AtSpi.StateType[id])
                    catch
                        names.Push(id)
                }
                return names
            }
        }

        /**
         * Returns True if this element currently has keyboard focus.
         * @returns {Boolean}
         */
        HasFocus {
            get {
                pSet := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_state_set")
                              , "Ptr", this.__ptr
                              , "Ptr")
                if !pSet
                    return false
                isFocused := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_state_set_contains")
                                   , "Ptr", pSet
                                   , "Int", AtSpi.StateType.Focused
                                   , "Int")
                AtSpi.__Unref(pSet)
                return isFocused
            }
        }

        /**
         * Attribute map (name -> value).
         * @returns {Map}
         */
        Attributes {
            get {
                err := 0
                pArr := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_attributes_as_array")
                              , "Ptr", this.__ptr
                              , "Ptr*", &err
                              , "Ptr")
                if err {
                    AtSpi.__FreeGError(err)
                    return Map()
                }
                attrs := Map()
                for _, item in AtSpi.__ReadGArrayStrings(pArr) {
                    if (pos := InStr(item, ":")) {
                        key := SubStr(item, 1, pos - 1)
                        val := SubStr(item, pos + 1)
                        attrs[key] := val
                    } else if item != "" {
                        attrs[item] := ""
                    }
                }
                return attrs
            }
        }

        /**
         * Supported interface names.
         * @returns {Array}
         */
        Interfaces {
            get {
                pArr := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_interfaces")
                              , "Ptr", this.__ptr
                              , "Ptr")
                return AtSpi.__ReadGArrayStrings(pArr)
            }
        }

        /**
         * Application accessible, or 0 if unavailable.
         * @returns {AtSpi.Accessible|0}
         */
        Application {
            get {
                err := 0
                p := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_application")
                           , "Ptr", this.__ptr
                           , "Ptr*", &err
                           , "Ptr")
                if err {
                    AtSpi.__FreeGError(err)
                    return 0
                }
                return p ? AtSpi.Accessible(p) : 0
            }
        }

        /**
         * Process id for the owning application.
         * @returns {Integer}
         */
        ProcessId {
            get {
                err := 0
                pid := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_process_id")
                             , "Ptr", this.__ptr
                             , "Ptr*", &err
                             , "UInt")
                if err {
                    AtSpi.__FreeGError(err)
                    return 0
                }
                return pid
            }
        }

        /**
         * Accessible numeric id.
         * @returns {Integer}
         */
        Id {
            get {
                err := 0
                id := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_id")
                            , "Ptr", this.__ptr
                            , "Ptr*", &err
                            , "Int")
                if err {
                    AtSpi.__FreeGError(err)
                    return -1
                }
                return id
            }
        }

        /**
         * Accessible id string (toolkit-defined).
         * @returns {String}
         */
        AccessibleId {
            get {
                err := 0
                p := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_accessible_id")
                           , "Ptr", this.__ptr
                           , "Ptr*", &err
                           , "Ptr")
                if err {
                    AtSpi.__FreeGError(err)
                    return ""
                }
                return AtSpi.__StrAndFree(p)
            }
        }

        /**
         * Help text for the element.
         * @returns {String}
         */
        HelpText {
            get {
                err := 0
                p := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_help_text")
                           , "Ptr", this.__ptr
                           , "Ptr*", &err
                           , "Ptr")
                if err {
                    AtSpi.__FreeGError(err)
                    return ""
                }
                return AtSpi.__StrAndFree(p)
            }
        }

        /**
         * Toolkit name.
         * @returns {String}
         */
        ToolkitName {
            get {
                err := 0
                p := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_toolkit_name")
                           , "Ptr", this.__ptr
                           , "Ptr*", &err
                           , "Ptr")
                if err {
                    AtSpi.__FreeGError(err)
                    return ""
                }
                return AtSpi.__StrAndFree(p)
            }
        }

        /**
         * Toolkit version string.
         * @returns {String}
         */
        ToolkitVersion {
            get {
                err := 0
                p := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_toolkit_version")
                           , "Ptr", this.__ptr
                           , "Ptr*", &err
                           , "Ptr")
                if err {
                    AtSpi.__FreeGError(err)
                    return ""
                }
                return AtSpi.__StrAndFree(p)
            }
        }

        /**
         * AT-SPI version string.
         * @returns {String}
         */
        AtspiVersion {
            get {
                err := 0
                p := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_atspi_version")
                           , "Ptr", this.__ptr
                           , "Ptr*", &err
                           , "Ptr")
                if err {
                    AtSpi.__FreeGError(err)
                    return ""
                }
                return AtSpi.__StrAndFree(p)
            }
        }

        /**
         * Object locale (LC_MESSAGES).
         * @returns {String}
         */
        ObjectLocale {
            get {
                err := 0
                p := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_object_locale")
                           , "Ptr", this.__ptr
                           , "Ptr*", &err
                           , "Ptr")
                if err {
                    AtSpi.__FreeGError(err)
                    return ""
                }
                return p ? StrGet(p, "UTF-8") : ""
            }
        }

        ; --- Interface availability ---
        /** @returns {Boolean} */
        IsAction => DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_is_action"), "Ptr", this.__ptr, "Int")
        /** @returns {Boolean} */
        IsApplication => DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_is_application"), "Ptr", this.__ptr, "Int")
        /** @returns {Boolean} */
        IsCollection => DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_is_collection"), "Ptr", this.__ptr, "Int")
        /** @returns {Boolean} */
        IsComponent => DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_is_component"), "Ptr", this.__ptr, "Int")
        /** @returns {Boolean} */
        IsDocument => DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_is_document"), "Ptr", this.__ptr, "Int")
        /** @returns {Boolean} */
        IsEditableText => DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_is_editable_text"), "Ptr", this.__ptr, "Int")
        /** @returns {Boolean} */
        IsHypertext => DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_is_hypertext"), "Ptr", this.__ptr, "Int")
        /** @returns {Boolean} */
        IsHyperlink => DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_is_hyperlink"), "Ptr", this.__ptr, "Int")
        /** @returns {Boolean} */
        IsImage => DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_is_image"), "Ptr", this.__ptr, "Int")
        /** @returns {Boolean} */
        IsSelection => DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_is_selection"), "Ptr", this.__ptr, "Int")
        /** @returns {Boolean} */
        IsTable => DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_is_table"), "Ptr", this.__ptr, "Int")
        /** @returns {Boolean} */
        IsTableCell => DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_is_table_cell"), "Ptr", this.__ptr, "Int")
        /** @returns {Boolean} */
        IsText => DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_is_text"), "Ptr", this.__ptr, "Int")
        /** @returns {Boolean} */
        IsValue => DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_is_value"), "Ptr", this.__ptr, "Int")

        ; --- Action interface ---
        /**
         * Available action names.
         * @returns {Array}
         */
        Actions {
            get {
                pAction := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_action_iface")
                                 , "Ptr", this.__ptr
                                 , "Ptr")
                if !pAction
                    return []
                err := 0
                n := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_action_get_n_actions")
                           , "Ptr", pAction
                           , "Ptr*", &err
                           , "Int")
                if err {
                    AtSpi.__FreeGError(err)
                    AtSpi.__Unref(pAction)
                    return []
                }
                actions := []
                Loop n {
                    err := 0
                    p := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_action_get_action_name")
                               , "Ptr", pAction
                               , "Int", A_Index - 1
                               , "Ptr*", &err
                               , "Ptr")
                    if err {
                        AtSpi.__FreeGError(err)
                        actions.Push("")
                    } else {
                        actions.Push(AtSpi.__StrAndFree(p))
                    }
                }
                AtSpi.__Unref(pAction)
                return actions
            }
        }

        /**
         * Performs an action by name or 1-based index.
         * @param action Action name or 1-based index.
         * @returns {Boolean}
         */
        DoAction(action) {
            pAction := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_action_iface")
                             , "Ptr", this.__ptr
                             , "Ptr")
            if !pAction
                return false
            idx := -1
            if IsInteger(action) {
                idx := action - 1
            } else {
                target := StrLower(action)
                for i, name in this.Actions
                    if StrLower(name) = target {
                        idx := i - 1
                        break
                    }
            }
            if idx < 0 {
                AtSpi.__Unref(pAction)
                return false
            }
            err := 0
            ok := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_action_do_action")
                        , "Ptr", pAction
                        , "Int", idx
                        , "Ptr*", &err
                        , "Int")
            if err {
                AtSpi.__FreeGError(err)
                ok := false
            }
            AtSpi.__Unref(pAction)
            return ok
        }

        /**
         * Performs the first available action.
         * @returns {Boolean}
         */
        DoDefaultAction() => this.Actions.Length ? this.DoAction(1) : false
        ; --- Component interface ---
        /**
         * Screen coordinates of the element.
         * @returns {{x:Integer,y:Integer,w:Integer,h:Integer}}
         */
        Location {
            get {
                ; AtspiComponent* iface
                pComp := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_component_iface")
                               , "Ptr", this.__ptr
                               , "Ptr")
                if !pComp
                    throw Error("Component interface not available")

                err := 0
                pRect := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_component_get_extents")
                               , "Ptr", pComp
                               , "Int", AtSpi.CoordType.Screen
                               , "Ptr*", &err
                               , "Ptr")

                AtSpi.__Unref(pComp)

                if err {
                    AtSpi.__FreeGError(err)
                    if pRect
                        DllCall(AtSpi.__Sym(AtSpi.LibGlib, "g_free"), "Ptr", pRect)
                    throw Error("Failed to get extents")
                }
                if !pRect
                    throw Error("Extents returned null")

                rx := NumGet(pRect,  0, "Int")
                ry := NumGet(pRect,  4, "Int")
                rw := NumGet(pRect,  8, "Int")
                rh := NumGet(pRect, 12, "Int")
                DllCall(AtSpi.__Sym(AtSpi.LibGlib, "g_free"), "Ptr", pRect)

                return { x: rx, y: ry, w: rw, h: rh }
            }
            set {
                if !IsObject(value)
                    throw TypeError("Location must be an object with x,y,w,h", -1)
                pComp := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_component_iface")
                               , "Ptr", this.__ptr
                               , "Ptr")
                if !pComp
                    throw Error("Component interface not available")
                err := 0
                ok := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_component_set_extents")
                            , "Ptr", pComp
                            , "Int", value.x
                            , "Int", value.y
                            , "Int", value.w
                            , "Int", value.h
                            , "Int", AtSpi.CoordType.Screen
                            , "Ptr*", &err
                            , "Int")
                AtSpi.__Unref(pComp)
                if err {
                    AtSpi.__FreeGError(err)
                    throw Error("Failed to set extents")
                }
                return !!ok
            }
        }

        /**
         * Attempts to grab keyboard focus for the element.
         * @returns {Boolean}
         */
        Focus() {
            pComp := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_component_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pComp
                return Error("Component interface not available")
            err := 0
            ok := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_component_grab_focus")
                        , "Ptr", pComp
                        , "Ptr*", &err
                        , "Int")
            AtSpi.__Unref(pComp)
            if err {
                AtSpi.__FreeGError(err)
                return false
            }
            return !!ok
        }

        /**
         * Scrolls the element into view based on an AtSpi.ScrollType.
         * @param scrollType AtSpi.ScrollType value or name.
         * @returns {Boolean}
         */
        ScrollTo(scrollType) {
            pComp := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_component_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pComp
                return Error("Component interface not available")
            if !IsInteger(scrollType)
                scrollType := AtSpi.ScrollType.%scrollType%
            err := 0
            ok := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_component_scroll_to")
                        , "Ptr", pComp
                        , "Int", scrollType
                        , "Ptr*", &err
                        , "Int")
            AtSpi.__Unref(pComp)
            if err {
                AtSpi.__FreeGError(err)
                return false
            }
            return !!ok
        }

        /**
         * Scrolls the element so that the point is in view.
         * @param x Screen X coordinate.
         * @param y Screen Y coordinate.
         * @returns {Boolean}
         */
        ScrollToPoint(x, y) {
            pComp := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_component_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pComp
                return Error("Component interface not available")
            err := 0
            ok := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_component_scroll_to_point")
                        , "Ptr", pComp
                        , "Int", AtSpi.CoordType.Screen
                        , "Int", x
                        , "Int", y
                        , "Ptr*", &err
                        , "Int")
            AtSpi.__Unref(pComp)
            if err {
                AtSpi.__FreeGError(err)
                return false
            }
            return !!ok
        }

        ; --- Value interface ---
        /**
         * Current numeric value.
         * @returns {Number}
         */
        Value {
            get => this.__ValueGet("atspi_value_get_current_value")
            set => this.__ValueSet(value)
        }

        /**
         * Text representation of the current value.
         * @returns {String}
         */
        Text {
            get {
                pVal := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_value_iface")
                              , "Ptr", this.__ptr
                              , "Ptr")
                if !pVal
                    throw Error("Value interface not available")
                err := 0
                pStr := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_value_get_text")
                              , "Ptr", pVal
                              , "Ptr*", &err
                              , "Ptr")
                AtSpi.__Unref(pVal)
                if err {
                    AtSpi.__FreeGError(err)
                    return ""
                }
                return AtSpi.__StrAndFree(pStr)
            }
            set {
                pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_editable_text_iface")
                            , "Ptr", this.__ptr
                            , "Ptr")
                if !pText
                    throw Error("EditableText interface not available")
                err := 0
                buf := Buffer(StrPut(value, "UTF-8"))
                StrPut(value, buf, "UTF-8")
                ok := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_editable_text_set_text_contents")
                            , "Ptr", pText
                            , "Ptr", buf.Ptr
                            , "Ptr*", &err
                            , "Int")
                AtSpi.__Unref(pText)
                if err {
                    AtSpi.__FreeGError(err)
                }
            }
        }

        /**
         * Minimum numeric value.
         * @returns {Number}
         */
        Minimum => this.__ValueGet("atspi_value_get_minimum_value")

        /**
         * Maximum numeric value.
         * @returns {Number}
         */
        Maximum => this.__ValueGet("atspi_value_get_maximum_value")

        /**
         * Minimum increment value.
         * @returns {Number}
         */
        MinimumIncrement => this.__ValueGet("atspi_value_get_minimum_increment")

        __ValueGet(funcName) {
            pVal := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_value_iface")
                          , "Ptr", this.__ptr
                          , "Ptr")
            if !pVal
                throw Error("Value interface not available")
            err := 0
            gVal := AtSpi.__NewGValueDouble()
            DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, funcName)
                  , "Ptr", pVal
                  , "Ptr", gVal.Ptr
                  , "Ptr*", &err
                  , "Int")
            AtSpi.__Unref(pVal)
            if err {
                AtSpi.__FreeGError(err)
                DllCall(AtSpi.__Sym(AtSpi.LibGObj, "g_value_unset"), "Ptr", gVal.Ptr)
                throw Error("Failed to get value")
            }
            value := DllCall(AtSpi.__Sym(AtSpi.LibGObj, "g_value_get_double")
                           , "Ptr", gVal.Ptr
                           , "Double")
            DllCall(AtSpi.__Sym(AtSpi.LibGObj, "g_value_unset"), "Ptr", gVal.Ptr)
            return value
        }

        __ValueSet(value) {
            pVal := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_value_iface")
                          , "Ptr", this.__ptr
                          , "Ptr")
            if !pVal
                throw Error("Value interface not available")
            err := 0
            gVal := AtSpi.__NewGValueDouble(value, true)
            ok := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_value_set_current_value")
                        , "Ptr", pVal
                        , "Ptr", gVal.Ptr
                        , "Ptr*", &err
                        , "Int")
            AtSpi.__Unref(pVal)
            DllCall(AtSpi.__Sym(AtSpi.LibGObj, "g_value_unset"), "Ptr", gVal.Ptr)
            if err {
                AtSpi.__FreeGError(err)
                throw Error("Failed to set current value")
            }
            return !!ok
        }

        ; --- EditableText interface ---
        /**
         * Inserts text at a position and updates the insertion point.
         * @param position 0-based insertion position.
         * @param text Text to insert.
         * @returns {Integer} New cursor position.
         */
        InsertText(position, text) {
            pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_editable_text_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pText
                throw Error("EditableText interface not available")
            err := 0
            buf := Buffer(StrPut(text, "UTF-8"))
            StrPut(text, buf, "UTF-8")
            pos := position
            ok := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_editable_text_insert_text")
                        , "Ptr", pText
                        , "Int", position
                        , "Ptr", buf.Ptr
                        , "Int", StrLen(text)
                        , "Int*", &pos
                        , "Ptr*", &err
                        , "Int")
            AtSpi.__Unref(pText)
            if err {
                AtSpi.__FreeGError(err)
                throw Error("Failed to insert text")
            }
            return pos
        }

        /**
         * Deletes text between positions.
         * @param startPos Start index (0-based).
         * @param endPos End index (0-based).
         * @returns {Boolean}
         */
        DeleteText(startPos, endPos) {
            pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_editable_text_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pText
                throw Error("EditableText interface not available")
            err := 0
            ok := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_editable_text_delete_text")
                        , "Ptr", pText
                        , "Int", startPos
                        , "Int", endPos
                        , "Ptr*", &err
                        , "Int")
            AtSpi.__Unref(pText)
            if err {
                AtSpi.__FreeGError(err)
                return false
            }
            return !!ok
        }

        /**
         * Copies text between positions to the clipboard.
         * @param startPos Start index (0-based).
         * @param endPos End index (0-based).
         * @returns {Boolean}
         */
        CopyText(startPos, endPos) {
            pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_editable_text_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pText
                throw Error("EditableText interface not available")
            err := 0
            ok := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_editable_text_copy_text")
                        , "Ptr", pText
                        , "Int", startPos
                        , "Int", endPos
                        , "Ptr*", &err
                        , "Int")
            AtSpi.__Unref(pText)
            if err {
                AtSpi.__FreeGError(err)
                return false
            }
            return !!ok
        }

        /**
         * Cuts text between positions to the clipboard.
         * @param startPos Start index (0-based).
         * @param endPos End index (0-based).
         * @returns {Boolean}
         */
        CutText(startPos, endPos) {
            pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_editable_text_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pText
                throw Error("EditableText interface not available")
            err := 0
            ok := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_editable_text_cut_text")
                        , "Ptr", pText
                        , "Int", startPos
                        , "Int", endPos
                        , "Ptr*", &err
                        , "Int")
            AtSpi.__Unref(pText)
            if err {
                AtSpi.__FreeGError(err)
                return false
            }
            return !!ok
        }

        /**
         * Pastes clipboard content at a position.
         * @param position 0-based insertion position.
         * @returns {Boolean}
         */
        PasteText(position) {
            pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_editable_text_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pText
                throw Error("EditableText interface not available")
            err := 0
            ok := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_editable_text_paste_text")
                        , "Ptr", pText
                        , "Int", position
                        , "Ptr*", &err
                        , "Int")
            AtSpi.__Unref(pText)
            if err {
                AtSpi.__FreeGError(err)
                return false
            }
            return !!ok
        }

        ; --- Text interface ---
        /**
         * Total character count.
         * @returns {Integer}
         */
        TextLength {
            get {
                pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_text_iface")
                               , "Ptr", this.__ptr
                               , "Ptr")
                if !pText
                    throw Error("Text interface not available")
                err := 0
                count := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_text_get_character_count")
                               , "Ptr", pText
                               , "Ptr*", &err
                               , "Int")
                AtSpi.__Unref(pText)
                if err {
                    AtSpi.__FreeGError(err)
                    return -1
                }
                return count
            }
        }

        /**
         * Caret offset (0-based).
         * @returns {Integer}
         */
        CaretIndex {
            get {
                pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_text_iface")
                               , "Ptr", this.__ptr
                               , "Ptr")
                if !pText
                    throw Error("Text interface not available")
                err := 0
                caret := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_text_get_caret_offset")
                               , "Ptr", pText
                               , "Ptr*", &err
                               , "Int")
                AtSpi.__Unref(pText)
                if err {
                    AtSpi.__FreeGError(err)
                    return -1
                }
                return caret
            }
        }

        /**
         * Returns text between offsets.
         * @param startOffset 0-based start offset.
         * @param endOffset 0-based end offset, or -1 for end.
         * @returns {String}
         */
        GetText(startOffset := 0, endOffset := -1) {
            pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_text_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pText
                throw Error("Text interface not available")
            err := 0
            pStr := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_text_get_text")
                          , "Ptr", pText
                          , "Int", startOffset
                          , "Int", endOffset
                          , "Ptr*", &err
                          , "Ptr")
            AtSpi.__Unref(pText)
            if err {
                AtSpi.__FreeGError(err)
                return ""
            }
            return AtSpi.__StrAndFree(pStr)
        }

        /**
         * Returns text at offset with boundary info.
         * @param offset 0-based offset.
         * @param boundaryType AtSpi.TextBoundaryType value or name.
         * @returns {{text:String,start:Integer,end:Integer}}
         */
        GetTextAtOffset(offset, boundaryType := "Char") {
            pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_text_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pText
                throw Error("Text interface not available")
            if !IsInteger(boundaryType)
                boundaryType := AtSpi.TextBoundaryType.%boundaryType%
            err := 0
            start := 0, end := 0
            pStr := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_text_get_text_at_offset")
                          , "Ptr", pText
                          , "Int", offset
                          , "Int", boundaryType
                          , "Int*", &start
                          , "Int*", &end
                          , "Ptr*", &err
                          , "Ptr")
            AtSpi.__Unref(pText)
            if err {
                AtSpi.__FreeGError(err)
                return {text:"", start:-1, end:-1}
            }
            return {text:AtSpi.__StrAndFree(pStr), start:start, end:end}
        }

        /**
         * Returns text before offset with boundary info.
         * @param offset 0-based offset.
         * @param boundaryType AtSpi.TextBoundaryType value or name.
         * @returns {{text:String,start:Integer,end:Integer}}
         */
        GetTextBeforeOffset(offset, boundaryType := "Char") {
            pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_text_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pText
                throw Error("Text interface not available")
            if !IsInteger(boundaryType)
                boundaryType := AtSpi.TextBoundaryType.%boundaryType%
            err := 0
            start := 0, end := 0
            pStr := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_text_get_text_before_offset")
                          , "Ptr", pText
                          , "Int", offset
                          , "Int", boundaryType
                          , "Int*", &start
                          , "Int*", &end
                          , "Ptr*", &err
                          , "Ptr")
            AtSpi.__Unref(pText)
            if err {
                AtSpi.__FreeGError(err)
                return {text:"", start:-1, end:-1}
            }
            return {text:AtSpi.__StrAndFree(pStr), start:start, end:end}
        }

        /**
         * Returns text after offset with boundary info.
         * @param offset 0-based offset.
         * @param boundaryType AtSpi.TextBoundaryType value or name.
         * @returns {{text:String,start:Integer,end:Integer}}
         */
        GetTextAfterOffset(offset, boundaryType := "Char") {
            pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_text_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pText
                throw Error("Text interface not available")
            if !IsInteger(boundaryType)
                boundaryType := AtSpi.TextBoundaryType.%boundaryType%
            err := 0
            start := 0, end := 0
            pStr := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_text_get_text_after_offset")
                          , "Ptr", pText
                          , "Int", offset
                          , "Int", boundaryType
                          , "Int*", &start
                          , "Int*", &end
                          , "Ptr*", &err
                          , "Ptr")
            AtSpi.__Unref(pText)
            if err {
                AtSpi.__FreeGError(err)
                return {text:"", start:-1, end:-1}
            }
            return {text:AtSpi.__StrAndFree(pStr), start:start, end:end}
        }

        /**
         * Number of selections.
         * @returns {Integer}
         */
        SelectionCount {
            get {
                pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_text_iface")
                               , "Ptr", this.__ptr
                               , "Ptr")
                if !pText
                    throw Error("Text interface not available")
                err := 0
                count := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_text_get_n_selections")
                               , "Ptr", pText
                               , "Ptr*", &err
                               , "Int")
                AtSpi.__Unref(pText)
                if err {
                    AtSpi.__FreeGError(err)
                    return -1
                }
                return count
            }
        }

        /**
         * Returns selection info.
         * @param index 1-based selection index.
         * @returns {{start:Integer,end:Integer,text:String}}
         */
        GetSelection(index := 1) {
            pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_text_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pText
                throw Error("Text interface not available")
            err := 0
            start := 0, end := 0
            pStr := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_text_get_selection")
                          , "Ptr", pText
                          , "Int", index - 1
                          , "Int*", &start
                          , "Int*", &end
                          , "Ptr*", &err
                          , "Ptr")
            AtSpi.__Unref(pText)
            if err {
                AtSpi.__FreeGError(err)
                throw Error("Failed to get selection")
            }
            return {start:start, end:end, text:AtSpi.__StrAndFree(pStr)}
        }

        /**
         * Adds a selection.
         * @param startPos Start index (0-based).
         * @param endPos End index (0-based).
         * @returns {Boolean}
         */
        AddSelection(startPos, endPos) {
            pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_text_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pText
                throw Error("Text interface not available")
            err := 0
            ok := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_text_add_selection")
                        , "Ptr", pText
                        , "Int", startPos
                        , "Int", endPos
                        , "Ptr*", &err
                        , "Int")
            AtSpi.__Unref(pText)
            if err {
                AtSpi.__FreeGError(err)
                return false
            }
            return !!ok
        }

        /**
         * Removes a selection by index.
         * @param index 1-based selection index.
         * @returns {Boolean}
         */
        RemoveSelection(index := 1) {
            pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_text_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pText
                throw Error("Text interface not available")
            err := 0
            ok := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_text_remove_selection")
                        , "Ptr", pText
                        , "Int", index - 1
                        , "Ptr*", &err
                        , "Int")
            AtSpi.__Unref(pText)
            if err {
                AtSpi.__FreeGError(err)
                return false
            }
            return !!ok
        }

        /**
         * Sets a selection by index.
         * @param index 1-based selection index.
         * @param startPos Start index (0-based).
         * @param endPos End index (0-based).
         * @returns {Boolean}
         */
        SetSelection(index, startPos, endPos) {
            pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_text_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pText
                throw Error("Text interface not available")
            err := 0
            ok := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_text_set_selection")
                        , "Ptr", pText
                        , "Int", index - 1
                        , "Int", startPos
                        , "Int", endPos
                        , "Ptr*", &err
                        , "Int")
            AtSpi.__Unref(pText)
            if err {
                AtSpi.__FreeGError(err)
                return false
            }
            return !!ok
        }

        /**
         * Gets the text offset at a screen point.
         * @param x Screen X coordinate.
         * @param y Screen Y coordinate.
         * @param coordType AtSpi.CoordType value or name.
         * @returns {Integer}
         */
        GetOffsetAtPoint(x, y, coordType := "Screen") {
            pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_text_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pText
                throw Error("Text interface not available")
            if !IsInteger(coordType)
                coordType := AtSpi.CoordType.%coordType%
            err := 0
            offset := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_text_get_offset_at_point")
                            , "Ptr", pText
                            , "Int", x
                            , "Int", y
                            , "Int", coordType
                            , "Ptr*", &err
                            , "Int")
            AtSpi.__Unref(pText)
            if err {
                AtSpi.__FreeGError(err)
                return -1
            }
            return offset
        }

        /**
         * Gets character extents for an offset.
         * @param offset 0-based character offset.
         * @param coordType AtSpi.CoordType value or name.
         * @returns {{x:Integer,y:Integer,w:Integer,h:Integer}}
         */
        GetCharacterExtents(offset, coordType := "Screen") {
            pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_text_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pText
                throw Error("Text interface not available")
            if !IsInteger(coordType)
                coordType := AtSpi.CoordType.%coordType%
            err := 0
            pRect := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_text_get_character_extents")
                           , "Ptr", pText
                           , "Int", offset
                           , "Int", coordType
                           , "Ptr*", &err
                           , "Ptr")
            AtSpi.__Unref(pText)
            if err {
                AtSpi.__FreeGError(err)
                if pRect
                    DllCall(AtSpi.__Sym(AtSpi.LibGlib, "g_free"), "Ptr", pRect)
                throw Error("Failed to get character extents")
            }
            if !pRect
                throw Error("Character extents returned null")
            rx := NumGet(pRect,  0, "Int")
            ry := NumGet(pRect,  4, "Int")
            rw := NumGet(pRect,  8, "Int")
            rh := NumGet(pRect, 12, "Int")
            DllCall(AtSpi.__Sym(AtSpi.LibGlib, "g_free"), "Ptr", pRect)
            return {x: rx, y: ry, w: rw, h: rh}
        }

        /**
         * Gets extents for a text range.
         * @param startOffset 0-based start offset.
         * @param endOffset 0-based end offset.
         * @param coordType AtSpi.CoordType value or name.
         * @returns {{x:Integer,y:Integer,w:Integer,h:Integer}}
         */
        GetRangeExtents(startOffset, endOffset, coordType := "Screen") {
            pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_text_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pText
                throw Error("Text interface not available")
            if !IsInteger(coordType)
                coordType := AtSpi.CoordType.%coordType%
            err := 0
            pRect := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_text_get_range_extents")
                           , "Ptr", pText
                           , "Int", startOffset
                           , "Int", endOffset
                           , "Int", coordType
                           , "Ptr*", &err
                           , "Ptr")
            AtSpi.__Unref(pText)
            if err {
                AtSpi.__FreeGError(err)
                if pRect
                    DllCall(AtSpi.__Sym(AtSpi.LibGlib, "g_free"), "Ptr", pRect)
                throw Error("Failed to get range extents")
            }
            if !pRect
                throw Error("Range extents returned null")
            rx := NumGet(pRect,  0, "Int")
            ry := NumGet(pRect,  4, "Int")
            rw := NumGet(pRect,  8, "Int")
            rh := NumGet(pRect, 12, "Int")
            DllCall(AtSpi.__Sym(AtSpi.LibGlib, "g_free"), "Ptr", pRect)
            return {x: rx, y: ry, w: rw, h: rh}
        }

        /**
         * Scrolls a text range into view.
         * @param startOffset 0-based start offset.
         * @param endOffset 0-based end offset.
         * @param scrollType AtSpi.ScrollType value or name.
         * @returns {Boolean}
         */
        ScrollSubstringTo(startOffset, endOffset, scrollType := "Anywhere") {
            pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_text_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pText
                throw Error("Text interface not available")
            if !IsInteger(scrollType)
                scrollType := AtSpi.ScrollType.%scrollType%
            err := 0
            ok := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_text_scroll_substring_to")
                        , "Ptr", pText
                        , "Int", startOffset
                        , "Int", endOffset
                        , "Int", scrollType
                        , "Ptr*", &err
                        , "Int")
            AtSpi.__Unref(pText)
            if err {
                AtSpi.__FreeGError(err)
                return false
            }
            return !!ok
        }

        /**
         * Scrolls a text range to a point.
         * @param startOffset 0-based start offset.
         * @param endOffset 0-based end offset.
         * @param x Screen X coordinate.
         * @param y Screen Y coordinate.
         * @param coordType AtSpi.CoordType value or name.
         * @returns {Boolean}
         */
        ScrollSubstringToPoint(startOffset, endOffset, x, y, coordType := "Screen") {
            pText := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_text_iface")
                           , "Ptr", this.__ptr
                           , "Ptr")
            if !pText
                throw Error("Text interface not available")
            if !IsInteger(coordType)
                coordType := AtSpi.CoordType.%coordType%
            err := 0
            ok := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_text_scroll_substring_to_point")
                        , "Ptr", pText
                        , "Int", startOffset
                        , "Int", endOffset
                        , "Int", coordType
                        , "Int", x
                        , "Int", y
                        , "Ptr*", &err
                        , "Int")
            AtSpi.__Unref(pText)
            if err {
                AtSpi.__FreeGError(err)
                return false
            }
            return !!ok
        }

        ; --- General element methods ---
        /**
         * True if the element is not defunct.
         * @returns {Boolean}
         */
        Exists {
            get {
                pSet := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_accessible_get_state_set")
                              , "Ptr", this.__ptr
                              , "Ptr")
                if !pSet
                    return false
                isDefunct := DllCall(AtSpi.__Sym(AtSpi.LibAtSpi, "atspi_state_set_contains")
                                   , "Ptr", pSet
                                   , "Int", AtSpi.StateType.Defunct
                                   , "Int")
                AtSpi.__Unref(pSet)
                return !isDefunct
            }
        }

        /**
         * Compares pointer identity with another accessible.
         * @param oCompare Other accessible.
         * @returns {Boolean}
         */
        IsEqual(oCompare) => oCompare is AtSpi.Accessible && this.Ptr == oCompare.Ptr

        /**
         * Finds the first element matching a set of conditions.
         * The returned element also has a "Path" property with the found element's path.
         * @param condition Condition object (see ValidateCondition).
         * @param scope The search scope (AtSpi.TreeScope value). Default is Descendants.
         * @param index Looks for the n-th element matching the condition.
         * @param order Tree traversal order (AtSpi.TreeTraversalOptions value).
         * @param depth Maximum depth for the search. Default is no limit.
         * @returns {AtSpi.Accessible}
         */
        FindElement(condition, scope:=4, index:=1, order:=0, depth:=-1) {
            if IsObject(condition) && !HasMethod(condition) {
                AtSpi.__ExtractNamedParameters(condition, "scope", &scope, "index", &index, "i", &index, "order", &order, "depth", &depth, "condition", &condition)
                for key in ["index", "scope", "depth", "order"]
                    if condition.HasOwnProp(key)
                        %key% := condition.%key%
                if condition.HasOwnProp("i")
                    index := condition.i
                if index < 0
                    order |= 2, index := -index
                else if index = 0
                    throw Error("Condition index cannot be 0", -1)
            }
            scope := IsInteger(scope) ? scope : AtSpi.TreeScope.%scope%
            order := IsInteger(order) ? order : AtSpi.TreeTraversalOptions.%order%

            if order&1
                return order&2 ? PostOrderLastToFirstRecursiveFind(this, condition, scope,, ++depth) : PostOrderFirstToLastRecursiveFind(this, condition, scope,, ++depth)
            if scope&1
                if this.ValidateCondition(condition) && (--index = 0)
                    return this.DefineProp("Path", {value:""})
            if scope>1
                return order&2 ? PreOrderLastToFirstRecursiveFind(this, condition, scope,, depth) : PreOrderFirstToLastRecursiveFind(this, condition, scope,, depth)

            PreOrderFirstToLastRecursiveFind(element, condition, scope:=4, path:="", depth:=-1) {
                --depth
                for i, child in element.Children {
                    if child.ValidateCondition(condition) && (--index = 0)
                        return child.DefineProp("Path", {value:path (path?",":"") i})
                    else if (scope&4) && (depth != 0) && (rf := PreOrderFirstToLastRecursiveFind(child, condition,, path (path?",":"") i, depth))
                        return rf 
                }
            }
            PreOrderLastToFirstRecursiveFind(element, condition, scope:=4, path:="", depth:=-1) {
                children := element.Children, length := children.Length + 1, --depth
                Loop (length - 1) {
                    child := children[length-A_index]
                    if child.ValidateCondition(condition) && (--index = 0)
                        return child.DefineProp("Path", {value:path (path?",":"") (length-A_index)})
                    else if scope&4 && (depth != 0) && (rf := PreOrderLastToFirstRecursiveFind(child, condition,, path (path?",":"") (length-A_index), depth))
                        return rf 
                }
            }
            PostOrderFirstToLastRecursiveFind(element, condition, scope:=4, path:="", depth:=-1) {
                if (--depth != 0) && scope>1 {
                    for i, child in element.Children {
                        if (rf := PostOrderFirstToLastRecursiveFind(child, condition, (scope & ~2)|1, path (path?",":"") i, depth))
                            return rf 
                    }
                }
                if scope&1 && element.ValidateCondition(condition) && (--index = 0)
                    return element.DefineProp("Path", {value:path})
            }
            PostOrderLastToFirstRecursiveFind(element, condition, scope:=4, path:="", depth:=-1) {
                if (--depth != 0) && scope>1 {
                    children := element.Children, length := children.Length + 1
                    Loop (length - 1) {
                        if (rf := PostOrderLastToFirstRecursiveFind(children[length-A_index], condition, (scope & ~2)|1, path (path?",":"") (length-A_index), depth))
                            return rf 
                    }
                }
                if scope&1 && element.ValidateCondition(condition) && (--index = 0)
                    return element.DefineProp("Path", {value:path})
            }
        }

        /**
         * Alias for FindElement.
         * @returns {AtSpi.Accessible}
         */
        FindFirst(args*) => this.FindElement(args*)

        /**
         * Returns an array of elements matching the condition.
         * The returned elements also have a "Path" property with the found elements path.
         * @param condition Condition object (see ValidateCondition). Default is True.
         * @param scope The search scope (AtSpi.TreeScope value). Default is Descendants.
         * @param depth Maximum depth for the search. Default is no limit.
         * @returns {Array}
         */
        FindElements(condition:=True, scope:=4, depth:=-1) {
            if IsObject(condition) && !HasMethod(condition) {
                AtSpi.__ExtractNamedParameters(condition, "scope", &scope, "depth", &depth, "condition", &condition)
                if condition.HasOwnProp("scope")
                    scope := condition.scope
                if condition.HasOwnProp("depth")
                    depth := condition.depth
            }

            matches := [], ++depth, scope := IsInteger(scope) ? scope : AtSpi.TreeScope.%scope%
            if scope&1
                if this.ValidateCondition(condition)
                    matches.Push(this.DefineProp("Path", {value:""}))
            if scope>1
                RecursiveFind(this, condition, (scope|1)^1, &matches,, depth)
            return matches
            RecursiveFind(element, condition, scope, &matches, path:="", depth:=-1) {
                if scope>1 {
                    --depth
                    for i, child in element.Children {
                        if child.ValidateCondition(condition)
                            matches.Push(child.DefineProp("Path", {value:path (path?",":"") i}))
                        if scope&4 && (depth != 0)
                            RecursiveFind(child, condition, scope, &matches, path (path?",":"") i, depth)
                    }
                }
            }
        }

        /**
         * Alias for FindElements.
         * @returns {Array}
         */
        FindAll(args*) => this.FindElements(args*)

        /**
         * Returns the first matching element or 0 if not found.
         * @returns {AtSpi.Accessible|0}
         */
        ElementExist(condition, scope:=4, index:=1, order:=0, depth:=-1) {
            try return this.FindElement(condition, scope, index, order, depth)
            catch
                return 0
        }

        /**
         * Waits for an element matching a condition or path to exist in the tree.
         * @param conditionOrPath Condition object or path string.
         * @param timeOut Timeout in milliseconds. Default is indefinite wait.
         * @param scope The search scope (AtSpi.TreeScope value). Default is Descendants.
         * @param index Looks for the n-th element matching the condition.
         * @param order Tree traversal order (AtSpi.TreeTraversalOptions value).
         * @param depth Maximum depth for the search. Default is no limit.
         * @returns {AtSpi.Accessible}
         */
        WaitElement(conditionOrPath, timeOut:=-1, scope:=4, index:=1, order:=0, depth:=-1) {
            if Type(conditionOrPath) = "Object" && conditionOrPath.HasOwnProp("timeOut")
                timeOut := conditionOrPath.timeOut
            waitTime := A_TickCount + timeOut
            while ((timeOut < 1) ? 1 : (A_tickCount < waitTime)) {
                try {
                    if IsObject(conditionOrPath)
                        return this.FindElement(conditionOrPath, scope, index, order, depth)
                    return this.ElementFromPath(conditionOrPath)
                }
                Sleep 40
            }
        }

        /**
         * Waits for a matching element to appear and be non-defunct.
         * @param conditionOrPath Condition object or path string.
         * @param timeOut Timeout in milliseconds. Default is indefinite wait.
         * @param scope The search scope (AtSpi.TreeScope value). Default is Descendants.
         * @param index Looks for the n-th element matching the condition.
         * @param order Tree traversal order (AtSpi.TreeTraversalOptions value).
         * @param depth Maximum depth for the search. Default is no limit.
         * @returns {AtSpi.Accessible}
         */
        WaitElementExist(conditionOrPath, timeOut:=-1, scope:=4, index:=1, order:=0, depth:=-1) {
            if Type(conditionOrPath) = "Object" && conditionOrPath.HasOwnProp("timeOut")
                timeOut := conditionOrPath.timeOut
            waitTime := A_TickCount + timeOut
            while ((timeOut < 1) ? 1 : (A_tickCount < waitTime)) {
                try {
                    oFind := IsObject(conditionOrPath) ? this.FindElement(conditionOrPath, scope, index, order, depth) : this.ElementFromPath(conditionOrPath)
                    if oFind.Exists
                        return oFind
                }
                Sleep 40
            }
        }

        /**
         * Tries to get an element from a path. If no element is found, an IndexError is thrown.
         * `ElementFromPath(path1[, path2, ...])`
         *
         * Paths can be:
         * 1. Comma-separated numeric path that defines which path to travel down the tree.
         *     Eg. `Element.ElementFromPath("3,2")` => selects Elements third child then its second child
         *
         * 2. A role token with optional index (RoleNameN) in the path.
         *     Eg. `Element.ElementFromPath("Button3,2")` => third Button child then its second child
         *
         * 3. A condition or conditions. The provided conditions define the route of tree-traversal,
         *    by default with Scope Children.
         *     Eg. `Element.ElementFromPath({RoleName:"Button"}, {RoleName:"List"})`
         *
         * @returns {AtSpi.Accessible}
         */
        ElementFromPath(paths*) {
            local err
            try return this[paths*]
            catch IndexError as err
                throw IndexError(StrReplace(err.Message, "at index", "at path index"), -1, err.Extra)
        }


        /**
         * Checks whether an element exists at a path and returns the element if one is found.
         * If no element is found, 0 is returned.
         * `ElementFromPathExist(path1[, path2, ...])`
         *
         * Paths can be:
         * 1. Comma-separated numeric path that defines which path to travel down the tree.
         *     Eg. `Element.ElementFromPathExist("3,2")` => checks for Elements third child then its second child
         *     Eg. `Element.ElementFromPathExist("Button3,2")` => checks for third Button child then its second child
         *
         * 2. A condition or conditions defining a route with Scope Children.
         *     Eg. `Element.ElementFromPathExist({RoleName:"Button"}, {RoleName:"List"})`
         *
         * @returns {AtSpi.Accessible|0}
         */
        ElementFromPathExist(paths*) {
            try return this[paths*]
            catch IndexError
                return 0
            return 0
        }

        /**
         * Wait element to appear at a path. The last argument may optionally be a timeout in milliseconds (default is indefinite wait).
         * `WaitElementFromPath(path1[, path2, ..., timeout])`
         *
         * Paths can be:
         * 1. Comma-separated numeric path that defines which path to travel down the tree.
         *     Eg. `Element.WaitElementFromPath("3,2", 2000)` => waits for Elements third child then its second child, timeout 2 seconds
         *     Eg. `Element.WaitElementFromPath("Button3,2")` => waits for third Button child then its second child
         *
         * 2. A condition or conditions defining a route with Scope Children.
         *     Eg. `Element.WaitElementFromPath({RoleName:"Button"}, {RoleName:"List"})`
         *
         * @returns {AtSpi.Accessible}
         */
        WaitElementFromPath(paths*) {
            local timeOut := -1, endtime, tick := 20
            if paths.Length > 1 && paths[paths.Length] is Integer {
                paths := paths.Clone()
                if paths[-2] is Integer
                    tick := paths.Pop()
                timeOut := paths.Pop()
            }
            endtime := A_TickCount + timeOut
            While ((timeOut == -1) || (A_Tickcount < endtime)) {
                try return this[paths*]
                Sleep tick
            }
        }

        /**
         * Waits for this element to not exist.
         * @param timeOut Timeout in milliseconds. Default is indefinite wait.
         * @returns {Boolean}
         */
        WaitNotExist(timeOut:=-1) {
            waitTime := A_TickCount + timeOut
            while ((timeOut < 1) ? 1 : (A_tickCount < waitTime)) {
                if !this.Exists
                    return 1
                Sleep 40
            }
        }

        /**
         * Returns the first ancestor (or self) that matches the condition.
         * @param condition Condition object (see ValidateCondition).
         * @returns {AtSpi.Accessible|0}
         */
        Normalize(condition) {
            if this.ValidateCondition(condition)
                return this
            oEl := this
            Loop {
                try {
                    oEl := oEl.Parent
                    if oEl.ValidateCondition(condition)
                        return oEl
                } catch
                    break
            }
            return 0
        }

        /**
         * Checks whether the element matches a provided condition.
         * @param oCond Condition object, array of conditions, or predicate function.
         * @returns {Boolean}
         */
        ValidateCondition(oCond) {
            if !IsObject(oCond)
                return !!oCond
            if HasMethod(oCond)
                return oCond(this)
            else if oCond is Array {
                for _, c in oCond
                    if this.ValidateCondition(c)
                        return 1
                return 0
            }
            matchmode := 3, casesensitive := 1
            for p in ["matchmode", "mm"]
                if oCond.HasOwnProp(p)
                    matchmode := oCond.%p%
            try matchmode := IsInteger(matchmode) ? matchmode : AtSpi.MatchMode.%matchmode%
            for p in ["casesensitive", "cs"]
                if oCond.HasOwnProp(p)
                    casesensitive := oCond.%p%
            for prop, cond in oCond.OwnProps() {
                switch Type(cond) {
                    case "String", "Integer":
                        if prop ~= "i)^(index|i|matchmode|mm|casesensitive|cs|scope|timeout|order|depth)$"
                            continue
                        propValue := ""
                        try propValue := this.%prop%
                        switch matchmode, 0 {
                            case 2:
                                if !InStr(propValue, cond, casesensitive)
                                    return 0
                            case 1:
                                if !((casesensitive && (SubStr(propValue, 1, StrLen(cond)) == cond)) || (!casesensitive && (SubStr(propValue, 1, StrLen(cond)) = cond)))
                                    return 0
                            case "Regex":
                                if !(propValue ~= cond)
                                    return 0
                            default:
                                if !((casesensitive && (propValue == cond)) || (!casesensitive && (propValue = cond)))
                                    return 0
                        }
                    case "AtSpi.Accessible":
                        if (prop="IsEqual") ? !this.IsEqual(cond) : !this.ValidateCondition(cond)
                            return 0
                    default:
                        if (HasProp(cond, "Length") ? cond.Length = 0 : ObjOwnPropCount(cond) = 0) {
                            try return this.%prop% && 0
                            catch
                                return 1
                        } else if (prop = "Location") {
                            try loc := this.Location
                            catch
                                return 0
                            for lprop, lval in cond.OwnProps() {
                                if (lprop = "relative" || lprop = "r")
                                    continue
                                if (loc.%lprop% != lval)
                                    return 0
                            }
                        } else if ((prop = "not") ? this.ValidateCondition(cond) : !this.ValidateCondition(cond))
                            return 0
                }
            }
            return 1
        }

        /**
         * Outputs relevant information about the element.
         * @param scope The search scope (AtSpi.TreeScope value). Default is Element.
         * @param delimiter The delimiter separating the outputted properties.
         * @param depth Maximum depth to dump. Default is no limit.
         * @returns {String}
         */
        Dump(scope:=1, delimiter:=" ", depth:=-1) {
            out := "", scope := IsInteger(scope) ? scope : AtSpi.TreeScope.%scope%
            if scope&1 {
                RoleName := "N/A", RoleId := "N/A", Name := "N/A", Description := "N/A"
                States := "", Interfaces := "", Location := {x:"N/A",y:"N/A",w:"N/A",h:"N/A"}, AccessibleId := ""
                try RoleName := this.RoleName
                try RoleId := this.RoleId
                try Name := this.Name
                try Description := this.Description
                try Location := this.Location
                try AccessibleId := this.AccessibleId
                try {
                    stateArr := this.States
                    if stateArr.Length
                        States := JoinArray(stateArr, ",")
                }
                try {
                    ifaceArr := this.Interfaces
                    if ifaceArr.Length
                        Interfaces := JoinArray(ifaceArr, ",")
                }
                out := "Role: " RoleName delimiter "RoleId: " RoleId delimiter "[Location: {x:" Location.x ",y:" Location.y ",w:" Location.w ",h:" Location.h "}]" delimiter "[Name: " Name "]"
                    . (Description ? delimiter "[Description: " Description "]" : "")
                    . (States ? delimiter "[States: " States "]" : "")
                    . (Interfaces ? delimiter "[Interfaces: " Interfaces "]" : "")
                    . (AccessibleId ? delimiter "[AccessibleId: " AccessibleId "]" : "") "`n"
            }
            if scope&4
                return Trim(RecurseTree(this, out,, depth), "`n")
            if scope&2 {
                for n, oChild in this.Children
                    out .= n ":" delimiter oChild.Dump() "`n"
            }
            return Trim(out, "`n")

            JoinArray(arr, delim) {
                out := ""
                for _, v in arr
                    out .= (out ? delim : "") v
                return out
            }

            RecurseTree(oAcc, tree, path:="", depth:=-1) {
                if depth > 0 {
                    StrReplace(path, ",",, , &count)
                    if count >= (depth-1)
                        return tree
                }
                try {
                    if !oAcc.ChildCount
                        return tree
                } catch
                    return tree

                for i, oChild in oAcc.Children {
                    tree .= path (path?",":"") i ":" delimiter oChild.Dump() "`n"
                    tree := RecurseTree(oChild, tree, path (path?",":"") i, depth)
                }
                return tree
            }
        }

        /**
         * Outputs relevant information about the element and all descendants.
         * @param delimiter The delimiter separating the outputted properties.
         * @param depth Maximum depth to dump. Default is no limit.
         * @returns {String}
         */
        DumpAll(delimiter:=" ", depth:=-1) => this.Dump(5, delimiter, depth)

        /**
         * Highlights the element for a chosen period of time.
         * @param showTime Unset/0/positive/negative/"clear".
         * @param color Border color. Default is red.
         * @param d Border thickness in pixels.
         * @returns {AtSpi.Accessible}
         */
        Highlight(showTime:=unset, color:="Red", d:=2) {
            if !AtSpi.__HighlightGuis.Has(this)
                AtSpi.__HighlightGuis[this] := []
            if (!IsSet(showTime) && AtSpi.__HighlightGuis[this].Length) || (IsSet(showTime) && showTime = "clear") {
                for _, r in AtSpi.__HighlightGuis[this]
                    r.Destroy()
                AtSpi.__HighlightGuis[this] := []
                return this
            } else if !IsSet(showTime)
                showTime := 2000
            try loc := this.Location
            if !IsSet(loc) || !IsObject(loc) || loc.w < 1 || loc.h < 1 || loc.x == -2147483648 || loc.y == -2147483648
                return this
            Loop 4
                AtSpi.__HighlightGuis[this].Push(Gui("+AlwaysOnTop -Caption +ToolWindow -DPIScale +E0x08000000"))
            Loop 4 {
                i := A_Index
                x1 := (i=2 ? loc.x+loc.w : loc.x-d)
                y1 := (i=3 ? loc.y+loc.h : loc.y-d)
                w1 := (i=1 or i=3 ? loc.w+2*d : d)
                h1 := (i=2 or i=4 ? loc.h+2*d : d)
                AtSpi.__HighlightGuis[this][i].BackColor := color
                AtSpi.__HighlightGuis[this][i].Show("NA x" . x1 . " y" . y1 . " w" . w1 . " h" . h1)
            }
            if showTime > 0 {
                Sleep(showTime)
                this.Highlight()
            } else if showTime < 0
                SetTimer(ObjBindMethod(this, "Highlight", "clear"), -Abs(showTime))
            return this
        }

        /**
         * Clears the highlight for this element.
         * @returns {AtSpi.Accessible}
         */
        ClearHighlight() => this.Highlight("clear")

        /**
         * Clicks the center of the element.
         * @param WhichButton Mouse button or delay if Integer.
         * @param ClickCount Number of clicks or "count delay" string.
         * @param DownOrUp Optional Down/Up component.
         * @param Relative Optional offset "x y".
         * @param NoActivate Currently ignored (no window activation).
         * @returns {AtSpi.Accessible}
         */
        Click(WhichButton:="left", ClickCount:=1, DownOrUp:="", Relative:="", NoActivate:=False) {
            rel := [0,0], pos := this.Location, saveCoordMode := A_CoordModeMouse, cCount := 1, SleepTime := -1
            if (Relative && !InStr(Relative, "rel"))
                rel := StrSplit(Relative, " "), Relative := ""
            if IsInteger(WhichButton)
                SleepTime := WhichButton, WhichButton := "left"
            if !IsInteger(ClickCount) && InStr(ClickCount, " ") {
                sCount := StrSplit(ClickCount, " ")
                cCount := sCount[1], SleepTime := sCount[2]
            } else if ClickCount > 9 {
                SleepTime := cCount, cCount := 1
            }
            CoordMode("Mouse", "Screen")
            Click(pos.x+pos.w//2+rel[1] " " pos.y+pos.h//2+rel[2] " " WhichButton (ClickCount ? " " ClickCount : "") (DownOrUp ? " " DownOrUp : "") (Relative ? " " Relative : ""))
            CoordMode("Mouse", saveCoordMode)
            Sleep(SleepTime)
            return this
        }
    }

    /**
     * Simple AT-SPI element viewer (tree + live inspection).
     */
    class Viewer {
        __New() {
            CoordMode "Mouse", "Screen"
            this.Stored := {mwId:0, FilteredTreeView:Map(), TreeView:Map()}
            this.OnlyVisibleElements := false
            this.Capturing := False
            this.gViewer := Gui("AlwaysOnTop Resize","AtSpiViewer")
            this.gViewer.OnEvent("Close", (*) => ExitApp())
            this.gViewer.OnEvent("Size", this.GetMethod("gViewer_Size").Bind(this))
            this.gViewer.Add("Text", "w100", "Window Info").SetFont("bold")
            this.LVWin := this.gViewer.Add("ListView", "h140 w250", ["Property", "Value"])
            this.LVWin.OnEvent("ContextMenu", LV_CopyTextMethod := this.GetMethod("LV_CopyText").Bind(this))
            this.LVWin.ModifyCol(1,100)
            this.LVWin.ModifyCol(2,140)
            for _, v in this.DefaultLVWinItems := ["Title", "Text", "Id", "Location", "Class(NN)", "Process", "PID"]
                this.LVWin.Add(,v,"")
            this.gViewer.Add("Text", "w100", "AtSpi Info").SetFont("bold")
            this.LVProps := this.gViewer.Add("ListView", "h220 w250", ["Property", "Value"])
            this.LVProps.OnEvent("ContextMenu", LV_CopyTextMethod)
            this.LVProps.ModifyCol(1,100)
            this.LVProps.ModifyCol(2,140)
            for _, v in this.DefaultLVPropsItems := ["RoleName", "RoleId", "Name", "Description", "States", "Location", "Interfaces", "AccessibleId", "ProcessId", "Id"]
                this.LVProps.Add(,v,"")
            this.ButCapture := this.gViewer.Add("Button", "xp+60 y+10 w130", "Start capturing (Alt+S)")
            this.ButCapture.OnEvent("Click", this.CaptureHotkeyFunc := this.GetMethod("ButCapture_Click").Bind(this))
            HotKey("!s", this.CaptureHotkeyFunc)
            this.SBMain := this.gViewer.Add("StatusBar",, "  Start capturing, then hold cursor still to construct tree")
            this.SBMain.OnEvent("Click", this.GetMethod("SBMain_Click").Bind(this))
            this.SBMain.OnEvent("ContextMenu", this.GetMethod("SBMain_Click").Bind(this))
            this.gViewer.Add("Text", "x278 y10 w200", "AT-SPI Tree").SetFont("bold")
            this.TVContext := this.gViewer.Add("TreeView", "x275 y35 w250 h390 -0x800")
            this.TVContext.OnEvent("ItemSelect", this.GetMethod("TVContext_Click").Bind(this))
            this.TVContext.OnEvent("ContextMenu", this.GetMethod("TVContext_ContextMenu").Bind(this))
            this.TVContext.Add("Start capturing to show tree")
            this.TextFilterTVContext := this.gViewer.Add("Text", "x275 y428", "Filter:")
            this.EditFilterTVContext := this.gViewer.Add("Edit", "x305 y425 w100")
            this.EditFilterTVContext.OnEvent("Change", this.GetMethod("EditFilterTV_Change").Bind(this))
            this.CBVisibleElements := this.gViewer.Add("Checkbox", "x+8 yp+4", "Visible elements")
            this.CBVisibleElements.OnEvent("Click", this.GetMethod("CBVisibleElements_Change").Bind(this))
            this.gViewer.Show()
        }

        gViewer_Size(GuiObj, MinMax, Width, Height) {
            this.SBMain.GetPos(,,, &SBHeight)
            bottomY := Height - SBHeight - GuiObj.MarginY
            this.TVContext.GetPos(&TVContextX, &TVContextY, &TVContextWidth, &TVContextHeight)
            this.EditFilterTVContext.GetPos(,,, &editH)
            this.TextFilterTVContext.GetPos(,,, &textH)
            this.CBVisibleElements.GetPos(,,, &cbH)
            filterY := bottomY - editH
            this.EditFilterTVContext.Move(TVContextX+30, filterY)
            this.TextFilterTVContext.Move(TVContextX, filterY + (editH - textH) // 2)
            this.CBVisibleElements.Move(TVContextX+140, filterY + (editH - cbH) // 2)
            gap := 10
            this.TVContext.Move(,,Width-TVContextX-10, filterY - TVContextY - gap)
            this.LVProps.GetPos(&LVPropsX, &LVPropsY, &LVPropsWidth, &LVPropsHeight)
            lvPropsHeight := bottomY - LVPropsY - 45
            this.LVProps.Move(,,,lvPropsHeight)
            this.ButCapture.Move(, LVPropsY + lvPropsHeight + 10)
        }

        ButCapture_Click(GuiCtrlObj?, Info?) {
            if this.Capturing {
                this.StopCapture()
                return
            }
            this.Capturing := True
            this.TVContext.Delete()
            this.TVContext.Add("Hold cursor still to construct tree")
            this.ButCapture.Text := "Stop capturing (Alt+S)"
            this.CaptureCallback := this.GetMethod("CaptureCycle").Bind(this)
            SetTimer(this.CaptureCallback, 200)
        }

        LV_CopyText(GuiCtrlObj, Info, *) {
            local out := "", LVData := Info > GuiCtrlObj.GetCount()
                ? ListViewGetContent("", GuiCtrlObj)
                : ListViewGetContent("Selected", GuiCtrlObj)
            for LVData in StrSplit(LVData, "`n") {
                LVData := StrSplit(LVData, "`t",,2)
                if LVData.Length < 2
                    continue
                switch LVData[1], 0 {
                    case "Location":
                        LVData[2] := "{" RegExReplace(LVData[2], "(\w:) (\d+)(?= )", "$1$2,") "}"
                }
                out .= ", " (GuiCtrlObj.Hwnd = this.LVWin.Hwnd ? "" : LVData[1] ":") (LVData[1] = "Location" || IsInteger(LVData[2]) ? LVData[2] : "`"" StrReplace(StrReplace(LVData[2], "``", "````"), "`"", "```"") "`"")
            }
            ToolTip("Copied: " (A_Clipboard := SubStr(out, 3)))
            SetTimer(ToolTip, -3000)
        }

        SBMain_Click(GuiCtrlObj, Info, *) {
            if InStr(this.SBMain.Text, "Path:") {
                ToolTip("Copied: " (A_Clipboard := SubStr(this.SBMain.Text, 9)))
                SetTimer((*) => ToolTip(), -3000)
            }
        }

        CBVisibleElements_Change(GuiCtrlObj, Info) {
            this.OnlyVisibleElements := GuiCtrlObj.Value
        }

        StopCapture(GuiCtrlObj:=0, Info:=0) {
            if this.Capturing {
                this.Capturing := False
                this.ButCapture.Text := "Start capturing (Alt+S)"
                SetTimer(this.CaptureCallback, 0)
                if this.Stored.HasOwnProp("oContext")
                    this.Stored.oContext.Highlight()
                return
            }
        }

        CaptureCycle() {
            Thread "NoTimers"
            MouseGetPos(&mX, &mY, &mwId)
            oContext := AtSpi.ElementFromPoint(mX, mY)
            if !IsObject(oContext) {
                AtSpi.ClearAllHighlights()
                this.LVWin.Delete()
                for v in this.DefaultLVWinItems
                    this.LVWin.Add(,v,"")
                this.LVProps.Delete()
                for v in this.DefaultLVPropsItems
                    this.LVProps.Add(,v,"")
                this.TVContext.Delete()
                this.TVContext.Add("No accessible at point")
                return
            }
            if this.Stored.HasOwnProp("oContext") && oContext.IsEqual(this.Stored.oContext) {
                if this.FoundTime != 0 && ((A_TickCount - this.FoundTime) > 1000) {
                    if (mX == this.Stored.mX) && (mY == this.Stored.mY)
                        this.ConstructTreeView(), this.FoundTime := 0
                    else
                        this.FoundTime := A_TickCount
                }
                this.Stored.mX := mX, this.Stored.mY := mY
                return
            }
            if !WinExist(mwId)
                return
            this.LVWin.Delete()
            WinGetPos(&mwX, &mwY, &mwW, &mwH, mwId)
            propsOrder := ["Title", "Text", "Id", "Location", "Class(NN)", "Process", "PID"]
            props := Map("Title", WinGetTitle(mwId), "Text", WinGetText(mwId), "Id", mwId, "Location", "x: " mwX " y: " mwY " w: " mwW " h: " mwH, "Class(NN)", WinGetClass(mwId), "Process", WinGetProcessName(mwId), "PID", WinGetPID(mwId))
            for propName in propsOrder
                this.LVWin.Add(,propName,props[propName])
            this.LVProps_Populate(oContext)
            this.Stored.mwId := mwId, this.Stored.oContext := oContext, this.Stored.mX := mX, this.Stored.mY := mY, this.FoundTime := A_TickCount
        }

        LVProps_Populate(oContext) {
            AtSpi.ClearAllHighlights()
            oContext.Highlight(0)
            this.LVProps.Delete()
            Location := {x:"N/A",y:"N/A",w:"N/A",h:"N/A"}, RoleName := "N/A", RoleId := "N/A", Name := "N/A", Description := "N/A", States := "N/A", Interfaces := "N/A", AccessibleId := "N/A", ProcessId := "N/A", Id := "N/A"
            for _, v in this.DefaultLVPropsItems {
                try {
                    if v = "Location"
                        Location := oContext.Location
                    else if v = "States" {
                        st := oContext.States
                        States := st.Length ? this.JoinArray(st, ",") : ""
                    } else if v = "Interfaces" {
                        it := oContext.Interfaces
                        Interfaces := it.Length ? this.JoinArray(it, ",") : ""
                    } else
                        %v% := oContext.%v%
                }
                this.LVProps.Add(,v, v = "Location" ? ("x: " Location.x " y: " Location.y " w: " Location.w " h: " Location.h) : %v%)
            }
        }

        TVContext_Click(GuiCtrlObj, Info) {
            if this.Capturing
                return
            try oContext := this.EditFilterTVContext.Value ? this.Stored.FilteredTreeView[Info] : this.Stored.TreeView[Info]
            if IsSet(oContext) && oContext {
                try this.SBMain.SetText("  Path: " oContext.Path)
                this.LVProps_Populate(oContext)
            }
        }

        TVContext_ContextMenu(GuiCtrlObj, Item, IsRightClick, X, Y) {
            TVContext_Menu := Menu()
            try oContext := this.EditFilterTVContext.Value ? this.Stored.FilteredTreeView[Item] : this.Stored.TreeView[Item]
            if IsSet(oContext)
                TVContext_Menu.Add("Copy to Clipboard", (*) => A_Clipboard := oContext.Dump())
            TVContext_Menu.Add("Copy Tree to Clipboard", (*) => A_Clipboard := AtSpi.ElementFromHandle(this.Stored.mwId).DumpAll())
            TVContext_Menu.Show()
        }

        EditFilterTV_Change(GuiCtrlObj, Info, *) {
            static TimeoutFunc := "", ChangeActive := False
            if !this.Stored.TreeView.Count
                return
            if (Info != "DoAction") || ChangeActive {
                if !TimeoutFunc
                    TimeoutFunc := this.GetMethod("EditFilterTV_Change").Bind(this, GuiCtrlObj, "DoAction")
                SetTimer(TimeoutFunc, -500)
                return
            }
            ChangeActive := True
            this.Stored.FilteredTreeView := Map(), parents := Map()
            if !(searchPhrase := this.EditFilterTVContext.Value) {
                this.ConstructTreeView()
                ChangeActive := False
                return
            }
            this.TVContext.Delete()
            temp := this.TVContext.Add("Searching...")
            Sleep -1
            this.TVContext.Opt("-Redraw")
            this.TVContext.Delete()
            for index, oContext in this.Stored.TreeView {
                for _, prop in this.DefaultLVPropsItems {
                    try {
                        if InStr(oContext.%Prop%, searchPhrase) {
                            if !parents.Has(prop)
                                parents[prop] := this.TVContext.Add(prop,, "Expand")
                            this.Stored.FilteredTreeView[this.TVContext.Add(this.GetShortDescription(oContext), parents[prop], "Expand")] := oContext
                        }
                    }
                }
            }
            if !this.Stored.FilteredTreeView.Count
                this.TVContext.Add("No results found matching `"" searchPhrase "`"")
            this.TVContext.Opt("+Redraw")
            TimeoutFunc := "", ChangeActive := False
        }

        ConstructTreeView() {
            this.TVContext.Delete()
            this.TVContext.Add("Constructing Tree, please wait...")
            Sleep -1
            this.TVContext.Opt("-Redraw")
            this.TVContext.Delete()
            this.Stored.TreeView := Map()
            if !WinExist(this.Stored.mwId)
                return
            this.RecurseTreeView(AtSpi.ElementFromHandle(this.Stored.mwId))
            this.TVContext.Opt("+Redraw")
            for k, v in this.Stored.TreeView
                if this.Stored.oContext.IsEqual(v)
                    this.TVContext.Modify(k, "Vis Select"), this.SBMain.SetText("  Path: " v.Path)
        }

        RecurseTreeView(oContext, parent:=0, path:="", depth := 0) {
            if AtSpi.MaxRecurseDepth >= 0 && ++depth > AtSpi.MaxRecurseDepth
                return
            if !oContext
                return
            this.Stored.TreeView[TWEl := this.TVContext.Add(this.GetShortDescription(oContext), parent, "Expand")] := oContext.DefineProp("Path", {value:path})
            i := 0
            for v in oContext.Children {
                if !this.OnlyVisibleElements || this.IsVisible(v)
                    ++i, this.RecurseTreeView(v, TWEl, path (path?",":"") i, depth)
            }
        }

        GetShortDescription(oContext) {
            elDesc := " `"`""
            try elDesc := " `"" oContext.Name "`""
            try elDesc := oContext.RoleName elDesc
            catch
                elDesc := "`"`"" elDesc
            return elDesc
        }

        IsVisible(oContext) {
            try {
                loc := oContext.Location
                if loc.X == -2147483648 || loc.Y == -2147483648
                    return false
                for _, id in oContext.StateIds {
                    if id = AtSpi.StateType.Visible
                        return true
                }
            } catch
                return false
            return false
        }

        JoinArray(arr, delim) {
            out := ""
            for _, v in arr
                out .= (out ? delim : "") v
            return out
        }
    }
}
