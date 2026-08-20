/*
    OCR library for AHK v2 / Keysharp (cross-platform).

    Purpose:
        Recognizes text in an image and returns a structured result (lines + words, each with its
        text and bounding box / position on screen), plus convenience helpers for highlighting,
        clicking, searching and sorting results. This is a bare-bones port of the UWP-based OCR.ahk
        for AutoHotkey, re-architected so the recognition *engine* is pluggable. The image is supplied
        by the cross-platform KS Image class, so OCR itself does no screen capture.

    Engines:
        OCR is engine-agnostic. The active engine is OCR.Engine; if left unset it defaults to an
        OCR.TesseractEngine instance (Tesseract running on Windows, Linux and macOS). To use a
        different backend (a cloud OCR, Windows.Media.Ocr, PaddleOCR, ...) just assign your own:

            OCR.Engine := MyEngine()

        An engine is ANY object implementing:

            Recognize(image, options) => Array of lines
                image   : a KS Image (already prepared). Read pixels via image.GetPixelData(1|4),
                          and image.Width / image.Height.
                options : { lang, datapath } (engine-specific hints; ignore what you don't need).
                returns : an Array where each element is one text line: an Array of word descriptors,
                          each an object { Text, x, y, w, h } in image-pixel coordinates.

        OCR turns that raw geometry into OCR.Result / OCR.Line / OCR.Word objects, normalizes the
        coordinates (capture scale + screen offset) and adds all the convenience helpers. So the
        three core features the framework guarantees regardless of engine are: the full text, the
        text line-by-line, and word-by-word with each word's position.

    Tesseract requirements (default engine):
        - Tesseract installed and its shared library reachable:
            Windows : libtesseract-5.dll (e.g. the UB-Mannheim installer's C:\Program Files\Tesseract-OCR)
            Linux   : libtesseract.so.5  (e.g. apt install tesseract-ocr)
            macOS   : libtesseract.dylib  (e.g. brew install tesseract)
          If not auto-detected, set the library path:  OCR.Engine.Library := "<full path>"
        - A language data file (e.g. eng.traineddata). OCR.Engine.DataPath can point at the tessdata
          folder; OCR.Language selects the language (default "eng").
          Note: on Windows keep tessdata at an ASCII path — Tesseract's own file I/O there cannot open
          non-ASCII paths (a Tesseract limitation; on Linux/macOS UTF-8 paths work fine).

    Basic usage:
        #include <OCR>
        result := OCR.FromRect(100, 100, 400, 200)   ; or OCR(Image.FromRect(...)) for an explicit Image
        MsgBox(result.Text)
        for word in result.Words
            word.Highlight(-1)
        result.FindString("Save").Click()

    Capture factories (thin wrappers over the matching Image.From* factory + OCR()):
        OCR.FromFile(FileName, Options?)
        OCR.FromBitmap(Bitmap, Options?)
        OCR.FromRect(X, Y, W, H, Options?)
        OCR.FromDesktop(Options?)
        OCR.FromMonitor(Monitor?, Options?)
        OCR.FromWindow(WinTitle?, Options?, WinText?, ExcludeTitle?, ExcludeText?)

    OCR(Bitmap, Options?) returns an OCR.Result:
        result.Text         => all recognized text
        result.Lines        => array of OCR.Line objects
        result.Words        => array of OCR.Word objects
        result.ImageWidth/Height
        result.FindString(Needle, Options?) / FindStrings / Filter(cb) / Crop(x1,y1,x2,y2)

    OCR.Line / OCR.Word:  .Text, .X, .Y, .Width, .Height, .BoundingRect (Word also has .Conf 0-100; Line also has .Words)
    Common methods (Result/Line/Word): .Highlight(showTime?, color:="Red", d:=2), .ClearHighlight(),
        .Click(WhichButton?, ClickCount?, DownOrUp?)

    Static helpers: OCR.GetVersion(), OCR.GetAvailableLanguages(), OCR.WaitText(...),
        OCR.WordsBoundingRect(words*), OCR.ClearAllHighlights(), OCR.Cluster(...),
        OCR.SortArray/ReverseArray/UniqueArray/FlattenArray.

    Options object for OCR(Bitmap, Options) (all optional): {lang, datapath, x, y, w, h, scale, rotate, flip}
        lang, datapath : forwarded to the engine.
        x, y, w, h     : crop rectangle in image pixels, applied before scaling (missing x/y default to 0;
                         missing w/h extend to the image edge).
        scale          : zoom factor applied after cropping (e.g. 2 upscales 2x) — improves recognition of
                         small text. Result coordinates are divided back by the factor.
        rotate         : clockwise degrees (90/180/270) applied to the pixels.
        flip           : "x" mirrors across the x-axis (vertical), "y" across the y-axis (horizontal).
      Result coordinates are reported in screen units: the capture origin (Image.OriginX/OriginY, adjusted
      for any crop) is added and the capture/scale factor divided out, so Highlight/Click land on screen
      with no extra arguments. Note: rotate/flip transform the pixels but coordinates are NOT mapped back through
      them, so after a rotate/flip the coordinates are in the transformed image's space.
*/

#Requires AutoHotkey v2

#import KS { Image, Highlight }

class OCR {
    static Version => "1.0.0"

    ; --- Generic configuration ---
    static Language := "eng"   ; default OCR language, forwarded to the engine (override per call via Options.lang)
    static Engine := ""        ; active recognition engine ("" = lazily create an OCR.TesseractEngine)

    static __Highlights := Map()   ; object -> its KS.Highlight border-overlay

    /**
     * Recognizes text in an image and returns an OCR.Result.
     * @param Bitmap A KS Image object, or anything Image.FromBitmap accepts (a bitmap handle / file).
     * @param Options Optional {lang, datapath, x, y, w, h, scale, rotate, flip}. See file header.
     * @returns {OCR.Result}
     */
    static Call(Bitmap, Options := 0) {
        ; Note: the local is named "img", not "image" — AHK variable names are case-insensitive, so a
        ; local "image" would shadow the imported Image class and break "Image.FromBitmap" below.
        local img := (Bitmap is Image) ? Bitmap : Image.FromBitmap(Bitmap)
        local lang := OCR.__GetOpt(Options, "lang", OCR.__GetOpt(Options, "language", OCR.Language))
        local datapath := OCR.__GetOpt(Options, "datapath", "")

        ; Optional pre-processing (crop / scale / rotate / flip). The transforms update the Image's scale
        ; and origin metadata, so the screen offset below is simply where the (possibly cropped) image now
        ; sits: Image.OriginX/OriginY. Image.FromWindow/FromRect/FromDesktop/FromMonitor record their
        ; top-left; file/handle images report 0. Highlight/Click then land on screen with no extra arguments.
        img := OCR.__ApplyTransforms(img, Options)

        local rawLines := OCR.__GetEngine().Recognize(img, {lang: lang, datapath: datapath})
        local result := OCR.__BuildResult(rawLines)
        result.ImageWidth := img.Width
        result.ImageHeight := img.Height
        OCR.__FinalizeResult(result, img.ScaleX, img.ScaleY, img.OriginX, img.OriginY)
        return result
    }

    /**
     * Applies the optional image pre-processing carried in Options, returning the image to OCR. When any
     * transform is requested the work is done on an independent Image.Copy() so the caller's Image is never
     * mutated; with no transforms the original image is returned untouched. Order matches OCR.ahk:
     * crop -> scale -> rotate -> flip.
     *
     * Options (all optional):
     *   x, y, w, h  Crop rectangle in image pixels, applied first. Missing x/y default to 0; missing w/h
     *               extend to the image edge. Result coordinates are mapped back through the crop offset.
     *   scale       Zoom factor (e.g. 2 upscales 2x). Improves recognition of small text; result
     *               coordinates are divided back by the factor so they still map to screen.
     *   rotate      Clockwise degrees (e.g. 90/180/270). Applied to the pixels; coordinates are NOT
     *               un-rotated, so after a rotate they are in the rotated image's space.
     *   flip        "x" mirrors across the x-axis (vertical flip); "y" mirrors across the y-axis
     *               (horizontal flip). Like rotate, coordinates are not mirrored back.
     */
    static __ApplyTransforms(img, Options) {
        local cx, cy, cw, ch, scale, rot, fl, hasCrop
        if !IsObject(Options)
            return img
        hasCrop := OCR.__HasOpt(Options, "x") || OCR.__HasOpt(Options, "y") || OCR.__HasOpt(Options, "w") || OCR.__HasOpt(Options, "h")
        scale := OCR.__GetOpt(Options, "scale", 1)
        rot := OCR.__GetOpt(Options, "rotate", 0)
        fl := OCR.__GetOpt(Options, "flip", 0)
        if !(hasCrop || scale != 1 || rot != 0 || (fl != 0 && fl != ""))
            return img
        ; Copy first: the Image transforms below mutate the instance, and the caller may keep using theirs.
        img := img.Copy()
        if hasCrop {
            cx := OCR.__GetOpt(Options, "x", 0), cy := OCR.__GetOpt(Options, "y", 0)
            cw := OCR.__GetOpt(Options, "w", img.Width - cx), ch := OCR.__GetOpt(Options, "h", img.Height - cy)
            img.Crop(cx, cy, cw, ch)
        }
        if (scale != 1)
            img.Scale(scale)
        if (rot != 0 || (fl != 0 && fl != "")) {
            ; Rotate/Flip invalidate the Image's screen mapping (OriginX/OriginY/ScaleX/ScaleY read ""
            ; afterwards). OCR's documented behavior is to keep reporting through the PRE-rotate mapping
            ; (coordinates come out in the transformed image's space, not un-rotated), so snapshot the
            ; mapping and re-anchor it explicitly with SetOrigin after the pixel transforms.
            local sx := img.ScaleX, sy := img.ScaleY, ox := img.OriginX, oy := img.OriginY
            if (rot != 0)
                img.Rotate(rot)
            if (fl != 0 && fl != "")
                img.Flip(fl = "y")   ; "y" -> mirror across the y-axis (horizontal); anything else -> across the x-axis (vertical)
            if (sx != "" && ox != "")
                img.SetOrigin(ox, oy, sx, sy)
        }
        return img
    }

    ;; ---------------------------------------------------------------------------------------------
    ;; Capture factories: thin wrappers over the matching Image.From* factory + OCR(), so you can
    ;; write OCR.FromWindow("A") instead of OCR(Image.FromWindow("A")). Each captures via the KS Image
    ;; class (which records the capture origin and scale on the Image), then OCRs it. The Options
    ;; object is forwarded to OCR() unchanged — see Call/header for its fields ({lang, datapath, x, y, w, h,
    ;; scale, rotate, flip}).
    ;; ---------------------------------------------------------------------------------------------

    /**
     * OCRs an image file (anything Image.FromFile accepts).
     * @param FileName Path to the image.
     * @param Options Optional {lang, datapath, x, y, w, h, scale, rotate, flip}. See file header.
     * @returns {OCR.Result}
     */
    static FromFile(FileName, Options := 0) => OCR(Image.FromFile(FileName), Options)

    /**
     * OCRs an existing bitmap (another KS Image, or a native bitmap handle).
     * @param Bitmap The bitmap source.
     * @param Options Optional {lang, datapath, x, y, w, h, scale, rotate, flip}. See file header.
     * @returns {OCR.Result}
     */
    static FromBitmap(Bitmap, Options := 0) => OCR(Image.FromBitmap(Bitmap), Options)

    /**
     * Captures a rectangle of the screen (absolute screen coordinates) and OCRs it. The capture's
     * top-left becomes the result's screen offset, so Highlight/Click land on screen with no extra args.
     * @param X,Y,W,H The screen rectangle.
     * @param Options Optional {lang, datapath, x, y, w, h, scale, rotate, flip}. See file header.
     * @returns {OCR.Result}
     */
    static FromRect(X, Y, W, H, Options := 0) => OCR(Image.FromRect(X, Y, W, H), Options)

    /**
     * Captures the whole virtual desktop (the union of all monitors) and OCRs it.
     * @param Options Optional {lang, datapath, x, y, w, h, scale, rotate, flip}. See file header.
     * @returns {OCR.Result}
     */
    static FromDesktop(Options := 0) => OCR(Image.FromDesktop(), Options)

    /**
     * Captures a single monitor (the primary monitor if Monitor is omitted) and OCRs it.
     * @param Monitor The 1-based monitor number, or unset for the primary monitor.
     * @param Options Optional {lang, datapath, x, y, w, h, scale, rotate, flip}. See file header.
     * @returns {OCR.Result}
     */
    static FromMonitor(Monitor?, Options := 0) => OCR(IsSet(Monitor) ? Image.FromMonitor(Monitor) : Image.FromMonitor(), Options)

    /**
     * Captures the window matched by the usual WinTitle criteria and OCRs it. The captured pixels'
     * on-screen origin becomes the result's screen offset, so Highlight/Click land on screen.
     * @param WinTitle,WinText,ExcludeTitle,ExcludeText Standard window-matching criteria.
     * @param Options Optional. Forwarded to OCR() ({lang, datapath, x, y, w, h, scale, rotate, flip}); its
     *   `mode`/`decorations` properties also select the Image.FromWindow capture technique (see Image.FromWindow).
     * @returns {OCR.Result}
     */
    static FromWindow(WinTitle := "", Options := 0, WinText := "", ExcludeTitle := "", ExcludeText := "") {
        ; Only forward Options as Image's capture-options when it's an object (so its mode/decorations are
        ; honored). A bare default 0 would be read by Image.FromWindow as capture mode 0, overriding its
        ; default mode 4 — so in that case let Image.FromWindow pick its own default.
        local img := IsObject(Options) ? Image.FromWindow(WinTitle, Options, WinText, ExcludeTitle, ExcludeText)
                                       : Image.FromWindow(WinTitle, , WinText, ExcludeTitle, ExcludeText)
        return OCR(img, Options)
    }

    ; Returns the active engine, creating the default Tesseract engine on first use.
    static __GetEngine() {
        if !OCR.Engine
            OCR.Engine := OCR.TesseractEngine()
        return OCR.Engine
    }

    ; Returns the engine's version/info string (if the engine exposes GetVersion), else "".
    static GetVersion() {
        local eng := OCR.__GetEngine()
        return eng.HasMethod("GetVersion") ? eng.GetVersion() : ""
    }

    ; Returns the languages the engine can use (if it exposes GetAvailableLanguages), else an empty array.
    static GetAvailableLanguages() {
        local eng := OCR.__GetEngine()
        return eng.HasMethod("GetAvailableLanguages") ? eng.GetAvailableLanguages() : []
    }

    ; Removes all highlights created by Highlight().
    static ClearAllHighlights() {
        local hl
        for _, hl in OCR.__Highlights
            try hl.Destroy()
        OCR.__Highlights := Map()
        return OCR
    }

    /**
     * Returns a bounding rectangle {x,y,w,h,x2,y2} enclosing the provided Word/Line objects.
     * @param words One or more objects with x,y,w,h. Requires at least one argument.
     */
    static WordsBoundingRect(words*) {
        if !words.Length
            throw ValueError("This function requires at least one argument", -1)
        local x1 := 100000000, y1 := 100000000, x2 := -100000000, y2 := -100000000, word
        for word in words
            x1 := Min(word.X, x1), y1 := Min(word.Y, y1), x2 := Max(word.X + word.Width, x2), y2 := Max(word.Y + word.Height, y2)
        return {X: x1, Y: y1, Width: x2 - x1, Height: y2 - y1, x2: x2, y2: y2}
    }

    /**
     * Repeatedly captures + OCRs until the needle text appears or the timeout elapses.
     * @param needle The searched text.
     * @param timeout Milliseconds; <= 0 waits indefinitely (default -1).
     * @param func A function returning an OCR.Result. Default OCRs the whole desktop.
     * @param casesense Case-sensitivity for the default comparison.
     * @param comparefunc Custom (haystack, needle) search; if given, casesense is ignored.
     * @returns {OCR.Result|""}
     */
    static WaitText(needle, timeout := -1, func?, casesense := false, comparefunc?) {
        local endTime := A_TickCount + timeout, result, line, total
        if !IsSet(func)
            func := () => OCR(Image.FromDesktop())
        if !IsSet(comparefunc)
            comparefunc := InStr.Bind(, , casesense)
        while (timeout > 0 ? (A_TickCount < endTime) : 1) {
            result := func(), total := ""
            for line in result.Lines
                total .= line.Text "`n"
            if comparefunc(Trim(total, "`n"), needle)
                return result
            ; Yield between attempts: capture+OCR back-to-back would peg a core and starve the message pump
            ; (hotkeys/SetTimer/GUI would all stall for the whole wait). The pause also gives the awaited text
            ; time to appear. This is a plain non-blocking Sleep — the timeout/exit is still governed above.
            Sleep(40)
        }
        return ""
    }

    class Common {
        x {
            get => this.BoundingRect.X
        }
        y {
            get => this.BoundingRect.Y
        }
        w {
            get => this.BoundingRect.Width
        }
        h {
            get => this.BoundingRect.Height
        }

        /**
         * Highlights the object on the screen with a colored border drawn as four edges on a single
         * transparent, click-through overlay window.
         * @param showTime Default 2000ms.
         *   Unset       - if already highlighted, removes it; otherwise highlights for 2 seconds.
         *   0           - indefinite highlight.
         *   positive ms - highlight and block for that long, then clear.
         *   negative ms - highlight for that long without blocking (auto-clears via a timer).
         *   "clear"     - remove this object's highlight.
         *   "clearall"  - remove every OCR highlight.
         * @param color Border color (default "Red").
         * @param d Border thickness in pixels (default 2).
         * @returns {this}
         */
        Highlight(showTime?, color := "Red", d := 2) {
            local x, y, w, h, hl
            if IsSet(showTime) {
                if (showTime = "clearall") {
                    OCR.ClearAllHighlights()
                    return this
                }
                if (showTime = "clear") {
                    if OCR.__Highlights.Has(this) {
                        try OCR.__Highlights[this].Destroy()
                        OCR.__Highlights.Delete(this)
                    }
                    return this
                }
            }
            if !IsSet(showTime) {
                if OCR.__Highlights.Has(this) {
                    try OCR.__Highlights[this].Destroy()
                    OCR.__Highlights.Delete(this)
                    return this
                }
                showTime := 2000
            }

            x := this.X, y := this.Y, w := this.Width, h := this.Height
            if this.HasProp("Relative") {
                x += this.Relative.HasProp("x") ? this.Relative.X : 0
                y += this.Relative.HasProp("y") ? this.Relative.Y : 0
            }
            if (w < 1 || h < 1)
                return this

            ; Draw via the built-in KS.Highlight overlay (a single transparent, click-through border window
            ; reused across moves), keyed by `this` so each element keeps its own. This centralizes the overlay
            ; (and its Wayland/KWin handling) instead of building a Gui here.
            hl := OCR.__Highlights.Has(this) ? OCR.__Highlights[this] : (OCR.__Highlights[this] := Highlight())
            hl.Color := color, hl.Thickness := d
            hl.Show(x, y, w, h)

            if (showTime > 0) {
                Sleep(showTime)
                this.Highlight()
            } else if (showTime < 0)
                SetTimer(ObjBindMethod(this, "Highlight", "clear"), -Abs(showTime))
            return this
        }

        ClearHighlight() => this.Highlight("clear")

        /**
         * Clicks the center of the object (in screen CoordMode). If the object has a Relative property
         * with x/y, those are added as an offset.
         */
        Click(WhichButton := "left", ClickCount := 1, DownOrUp := "") {
            local x := this.X, y := this.Y, w := this.Width, h := this.Height, cx, cy, saveCoordMode
            if this.HasProp("Relative") {
                x += this.Relative.HasProp("x") ? this.Relative.X : 0
                y += this.Relative.HasProp("y") ? this.Relative.Y : 0
            }
            cx := x + w // 2, cy := y + h // 2
            saveCoordMode := A_CoordModeMouse
            CoordMode("Mouse", "Screen")
            Click(cx " " cy " " WhichButton (ClickCount != "" ? " " ClickCount : "") (DownOrUp ? " " DownOrUp : ""))
            CoordMode("Mouse", saveCoordMode)
            return this
        }

        /**
         * Adds an offset to every Word's coordinates (and the object's own if it has Words). Useful to
         * convert from image-relative to screen coordinates after the fact.
         */
        OffsetCoordinates(offsetX, offsetY) {
            local word
            if (offsetX = 0 && offsetY = 0)
                return this
            if this.HasProp("Words")
                for word in this.Words
                    OCR.__SetRect(word, word.X + offsetX, word.Y + offsetY, word.Width, word.Height)
            return this
        }
    }

    class Result extends OCR.Common {
        /**
         * Finds the first occurrence of Needle and returns a new OCR.Result containing only the match.
         * Partial matches return the whole word ("wo" in "hello world" -> "world").
         * @param Needle The string to find.
         * @param Options {CaseSense:false, IgnoreLinebreaks:false, AllowOverlap:false, i:1, x, y, w, h, SearchFunc}
         */
        FindString(Needle, Options := "") => this.__FindString(Needle, Options, false)

        /**
         * Finds all occurrences of Needle and returns an array of OCR.Result objects.
         */
        FindStrings(Needle, Options := "") => this.__FindString(Needle, Options, true)

        __FindString(Needle, Options, All) {
            local CaseSense := false, IgnoreLinebreaks := false, AllowOverlap := false, i := 1, SearchFunc, x, y, w, h
            local fullHaystackLinebreaks := "`n", offset := 0, line, counter := 0, x1, y1, x2, y2, result, results := [], word
            local tokenizedHaystack, fullHaystackNoLinebreaks, fullHaystack, fullFirst, fullLast, currentHaystack
            local loc, foundNeedle, foundLen, tokenizedNeedle, wsNeedle, wsSplit, lbNeedle, lbSplit, preceding, startingWord
            local foundWords, foundLines

            if !(Needle is String)
                throw TypeError("Needle is required to be a string, not type " Type(Needle), -1)
            if (Trim(Needle, " `t`n`r") == "")
                throw ValueError("Needle cannot be an empty string", -1)

            CaseSense := OCR.__GetOpt(Options, "CaseSense", CaseSense)
            IgnoreLinebreaks := OCR.__GetOpt(Options, "IgnoreLinebreaks", IgnoreLinebreaks)
            AllowOverlap := OCR.__GetOpt(Options, "AllowOverlap", AllowOverlap)
            i := OCR.__GetOpt(Options, "i", i)
            if OCR.__HasOpt(Options, "SearchFunc")
                SearchFunc := Options.SearchFunc
            if OCR.__HasOpt(Options, "x")
                x := Options.X
            if OCR.__HasOpt(Options, "y")
                y := Options.Y
            if OCR.__HasOpt(Options, "w")
                w := Options.Width
            if OCR.__HasOpt(Options, "h")
                h := Options.Height

            if !IsSet(SearchFunc)
                SearchFunc := (haystack, needle, &foundstr) => (pos := InStr(haystack, needle, CaseSense), foundstr := SubStr(haystack, pos, StrLen(needle)), pos)

            if (IsSet(x) || IsSet(y) || IsSet(w) || IsSet(h))
                x1 := x ?? -100000, y1 := y ?? -100000, x2 := IsSet(w) ? x + w : 100000, y2 := IsSet(h) ? y + h : 100000

            tokenizedHaystack := [IgnoreLinebreaks ? " " : "`n"]
            for line in this.Lines {
                fullHaystackLinebreaks .= line.Text "`n"
                for word in line.Words
                    tokenizedHaystack.Push(word, " ")
                tokenizedHaystack.Pop()
                tokenizedHaystack.Push(IgnoreLinebreaks ? " " : "`n")
            }

            fullHaystackNoLinebreaks := StrReplace(fullHaystackLinebreaks, "`n", " ")
            fullHaystack := IgnoreLinebreaks ? fullHaystackNoLinebreaks : fullHaystackLinebreaks

            Needle := RegExReplace(StrReplace(Needle, "`t", " "), " +", " ")
            fullFirst := SubStr(Needle, 1, 1) ~= "[ \n]", fullLast := SubStr(Needle, -1, 1) ~= "[ \n]"

            currentHaystack := fullHaystack
            Loop {
                if !(loc := SearchFunc(currentHaystack, Needle, &foundNeedle))
                    break
                if IsObject(foundNeedle)
                    foundNeedle := foundNeedle[]

                foundLen := AllowOverlap ? 1 : StrLen(foundNeedle)
                currentHaystack := SubStr(currentHaystack, loc + foundLen)
                offset += loc + foundLen - 1

                if (++counter < i)
                    continue

                tokenizedNeedle := []
                for wsNeedle in wsSplit := StrSplit(foundNeedle, " ") {
                    for lbNeedle in lbSplit := StrSplit(wsNeedle, "`n")
                        tokenizedNeedle.Push(lbNeedle, "`n")
                    if lbSplit.Length
                        tokenizedNeedle.Pop()
                    tokenizedNeedle.Push(" ")
                }
                tokenizedNeedle.Pop()

                preceding := SubStr(fullHaystackNoLinebreaks, 1, offset - foundLen)
                StrReplace(preceding, " ", , , &startingWord := 0)
                startingWord := startingWord * 2 + fullFirst - 1

                foundNeedle := "", foundWords := [], foundLines := [], line := OCR.Line()
                line.Words := [], line.Text := ""
                Loop tokenizedNeedle.Length {
                    word := tokenizedHaystack[startingWord + A_Index]
                    if (word == "`n") {
                        foundNeedle .= line.Text
                        line.Text := RTrim(line.Text)
                        if line.Words.Length
                            OCR.__SetRect(line, OCR.WordsBoundingRect(line.Words*))
                        foundLines.Push(line)
                        line := OCR.Line()
                        line.Words := [], line.Text := ""
                    }
                    if !IsObject(word)
                        continue
                    if (IsSet(x1) && (word.X < x1 || word.Y < y1 || word.X + word.Width > x2 || word.Y + word.Height > y2)) {
                        counter--
                        continue 2
                    }
                    line.Words.Push(word), line.Text := line.Text word.Text " "
                }
                if (line.Text != "") {
                    foundNeedle .= line.Text
                    line.Text := RTrim(line.Text)
                    if line.Words.Length
                        OCR.__SetRect(line, OCR.WordsBoundingRect(line.Words*))
                    foundLines.Push(line)
                }

                result := OCR.Result()
                result.ImageWidth := this.ImageWidth
                result.ImageHeight := this.ImageHeight
                result.Lines := foundLines
                result.Words := foundWords := this.__CollectWords(foundLines)
                result.Text := foundNeedle
                if foundWords.Length
                    OCR.__SetRect(result, OCR.WordsBoundingRect(foundWords*))
                else
                    OCR.__SetRect(result, {X: 0, Y: 0, Width: 0, Height: 0})

                if All
                    results.Push(result)
                else
                    return result
            }
            if All
                return results
            throw TargetError('The target string "' Needle '" was not found', -1)
        }

        /**
         * Returns a new OCR.Result containing only the words for which callback(word) is truthy.
         */
        Filter(callback) {
            if !HasMethod(callback)
                throw ValueError("Filter callback must be a function", -1)
            local line, word, croppedLines := [], croppedWords, lineText, allWords := [], nl, txt := "", result
            for line in this.Lines {
                croppedWords := [], lineText := ""
                for word in line.Words
                    if callback(word)
                        croppedWords.Push(word), allWords.Push(word), lineText .= word.Text " "
                if croppedWords.Length {
                    nl := OCR.Line()
                    nl.Words := croppedWords
                    nl.Text := Trim(lineText)
                    OCR.__SetRect(nl, OCR.WordsBoundingRect(croppedWords*))
                    croppedLines.Push(nl)
                }
            }
            result := OCR.Result()
            result.ImageWidth := this.ImageWidth
            result.ImageHeight := this.ImageHeight
            result.Lines := croppedLines
            result.Words := allWords
            for line in croppedLines
                txt .= line.Text "`n"
            result.Text := RTrim(txt, "`n")
            if allWords.Length
                OCR.__SetRect(result, OCR.WordsBoundingRect(allWords*))
            else
                OCR.__SetRect(result, {X: 0, Y: 0, Width: 0, Height: 0})
            return result
        }

        /**
         * Crops the result to words fully inside the rectangle defined by points (x1,y1) and (x2,y2).
         * Coordinates are relative to the result object (same space as the words).
         */
        Crop(x1 := -100000, y1 := -100000, x2 := 100000, y2 := 100000)
            => this.Filter((word) => word.X >= x1 && word.Y >= y1 && (word.X + word.Width) <= x2 && (word.Y + word.Height) <= y2)

        __CollectWords(lines) {
            local words := [], line, word
            for line in lines
                for word in line.Words
                    words.Push(word)
            return words
        }
    }

    class Line extends OCR.Common {
    }

    class Word extends OCR.Common {
    }

    ;; ---------------------------------------------------------------------------------------------
    ;; Sorting / clustering helpers (ported from OCR.ahk)
    ;; ---------------------------------------------------------------------------------------------

    /**
     * Clusters objects (Words/Lines) into lines using a 2D DBSCAN. Returns an array of objects with
     * {x,y,w,h,Text,Words}. See OCR.ahk for full parameter docs.
     */
    static Cluster(objs, eps_x := -1, eps_y := -1, minPts := 1, compareFunc?, &noise?) {
        local clusters := [], cluster, word, point, br, sum := 0, t
        local visited := Map(), clustered := Map(), C := [], c_n := 0, neighbourPts := []
        noise := IsSet(noise) && (noise is Array) ? noise : []
        if !IsObject(objs) || !(objs is Array)
            throw ValueError("objs argument must be an Array", -1)
        if !objs.Length
            return []
        if (IsSet(compareFunc) && !HasMethod(compareFunc))
            throw ValueError("compareFunc must be a valid function", -1)

        if !IsSet(compareFunc) {
            if (eps_y < 0) {
                for point in objs
                    sum += point.Height
                eps_y := (sum // objs.Length) // 2
            }
            compareFunc := (p1, p2) => Abs(p1.Y + p1.Height // 2 - p2.Y - p2.Height // 2) < eps_y && (eps_x < 0 || (Abs(p1.X + p1.Width - p2.X) < eps_x || Abs(p1.X - p2.X - p2.Width) < eps_x))
        }

        for point in objs {
            visited[point] := 1, neighbourPts := [], RegionQuery(point)
            if !clustered.Has(point) {
                C.Push([]), c_n += 1, C[c_n].Push(point), clustered[point] := 1
                ExpandCluster(point)
            }
            if (C[c_n].Length < minPts)
                noise.Push(C[c_n]), C.RemoveAt(c_n), c_n--
        }

        for cluster in C {
            OCR.SortArray(cluster, , "x")
            br := OCR.Common()
            br.BoundingRect := OCR.WordsBoundingRect(cluster*)
            br.Words := cluster
            t := ""
            for word in cluster
                t .= word.Text " "
            br.Text := RTrim(t)
            clusters.Push(br)
        }
        OCR.SortArray(clusters, , "y")
        return clusters

        ExpandCluster(P) {
            local point
            for point in neighbourPts {
                if !visited.Has(point) {
                    visited[point] := 1, RegionQuery(point)
                    if !clustered.Has(point)
                        C[c_n].Push(point), clustered[point] := 1
                }
            }
        }
        RegionQuery(P) {
            local point
            for point in objs
                if !visited.Has(point)
                    if compareFunc(P, point)
                        neighbourPts.Push(point)
        }
    }

    /**
     * Sorts an array in-place. optionsOrCallback: "N" numeric (default), "C"/"C1"/"COn" case-sensitive,
     * "C0"/"COff" case-insensitive, "Random", or a custom comparator. key optionally sorts by obj.%key%.
     */
    static SortArray(arr, optionsOrCallback := "N", key?) {
        local compareFunc, len, i, j, tmp
        if (arr.Length < 2)
            return arr
        if HasMethod(optionsOrCallback)
            compareFunc := optionsOrCallback, optionsOrCallback := ""
        else {
            if InStr(optionsOrCallback, "N")
                compareFunc := IsSet(key) ? NumericCompareKey.Bind(key) : NumericCompare
            if RegExMatch(optionsOrCallback, "i)C(?!0)|C1|COn")
                compareFunc := IsSet(key) ? StringCompareKey.Bind(key, , true) : StringCompare.Bind(, , true)
            if RegExMatch(optionsOrCallback, "i)C0|COff")
                compareFunc := IsSet(key) ? StringCompareKey.Bind(key) : StringCompare
            if InStr(optionsOrCallback, "Random") {
                len := arr.Length
                Loop len - 1 {
                    i := len + 1 - A_Index
                    j := Random(1, i)
                    if (j != i)
                        tmp := arr[i], arr[i] := arr[j], arr[j] := tmp
                }
                return arr
            }
            if !IsSet(compareFunc)
                throw ValueError("No valid options provided!", -1)
        }
        QuickSort(1, arr.Length)
        if RegExMatch(optionsOrCallback, "i)R(?!a)")
            OCR.ReverseArray(arr)
        if InStr(optionsOrCallback, "U")
            arr := OCR.UniqueArray(arr)
        return arr

        NumericCompare(left, right) => (left > right) - (left < right)
        NumericCompareKey(key, left, right) => ((f1 := left.HasProp("__Item") ? left[key] : left.%key%), (f2 := right.HasProp("__Item") ? right[key] : right.%key%), (f1 > f2) - (f1 < f2))
        StringCompare(left, right, casesense := false) => StrCompare(left "", right "", casesense)
        StringCompareKey(key, left, right, casesense := false) => StrCompare((left.HasProp("__Item") ? left[key] : left.%key%) "", (right.HasProp("__Item") ? right[key] : right.%key%) "", casesense)

        QuickSort(left, right) {
            local i := left, j := right, pivot := arr[(left + right) // 2], temp
            while (i <= j) {
                while (compareFunc(arr[i], pivot) < 0)
                    i++
                while (compareFunc(arr[j], pivot) > 0)
                    j--
                if (i <= j) {
                    temp := arr[i], arr[i] := arr[j], arr[j] := temp
                    i++, j--
                }
            }
            if (left < j)
                QuickSort(left, j)
            if (i < right)
                QuickSort(i, right)
        }
    }

    ; Reverses an array in-place.
    static ReverseArray(arr) {
        local len := arr.Length + 1, max := (len // 2), i := 0, temp
        while (++i <= max)
            temp := arr[len - i], arr[len - i] := arr[i], arr[i] := temp
        return arr
    }

    ; Returns a new array with only unique values.
    static UniqueArray(arr) {
        local unique := Map(), v
        for v in arr
            unique[v] := 1
        return [unique*]
    }

    ; Flattens a (possibly nested) array into a one-dimensional array.
    static FlattenArray(arr) {
        local r := [], v
        for v in arr {
            if (v is Array)
                r.Push(OCR.FlattenArray(v)*)
            else
                r.Push(v)
        }
        return r
    }

    ;; ---------------------------------------------------------------------------------------------
    ;; Internal: engine-agnostic result construction
    ;; ---------------------------------------------------------------------------------------------

    ; Builds an OCR.Result from an engine's raw output (an array of lines, each an array of word
    ; descriptors {Text, x, y, w, h} in image-pixel coordinates).
    static __BuildResult(rawLines) {
        local lineObjs := [], allWords := [], fullText := "", rawWords, rw, words, word, text, line, result
        for rawWords in rawLines {
            if !(rawWords is Array) || !rawWords.Length
                continue
            words := [], text := ""
            for rw in rawWords {
                word := OCR.Word()
                word.Text := rw.Text
                OCR.__SetRect(word, rw.X, rw.Y, rw.Width, rw.Height)
                ; Conf is 0-100 recognition confidence (Tesseract); "" for engines that don't report it.
                word.Conf := rw.HasProp("Conf") ? rw.Conf : ""
                words.Push(word), allWords.Push(word)
                text .= rw.Text " "
            }
            text := RTrim(text)
            line := OCR.Line()
            line.Words := words
            line.Text := text
            lineObjs.Push(line)
            fullText .= text "`n"
        }

        result := OCR.Result()
        result.Lines := lineObjs
        result.Words := allWords
        result.Text := RTrim(fullText, "`n")
        return result
    }

	; Normalizes word coordinates (image pixels -> native screen units + screen offset), then
    ; computes line/result bounding rects from the final word coordinates. sx/sy are the image's per-axis
    ; ScaleX/ScaleY; this divide assumes those axes still align with the image, which holds for OCR's own
    ; `scale` Option (always a single isotropic factor). A manual anisotropic Image.Scale(sx, sy) followed
    ; by a 90/270 Image.Rotate would swap the pixel axes but not ScaleX/ScaleY, so the divide would use the
    ; wrong factor per axis — combine anisotropic scaling with rotate only on an image not fed back to OCR.
    static __FinalizeResult(result, sx, sy, ox, oy) {
        local word, line
        if (sx != 1 || sy != 1 || ox != 0 || oy != 0)
            for word in result.Words
                OCR.__SetRect(word, Integer(word.X / sx) + ox, Integer(word.Y / sy) + oy, Integer(word.Width / sx), Integer(word.Height / sy))
        for line in result.Lines
            OCR.__SetRect(line, line.Words.Length ? OCR.WordsBoundingRect(line.Words*) : {X: 0, Y: 0, Width: 0, Height: 0})
        OCR.__SetRect(result, result.Words.Length ? OCR.WordsBoundingRect(result.Words*) : {X: 0, Y: 0, Width: 0, Height: 0})
        return result
    }

    ; Sets x/y/w/h and BoundingRect on an object. Accepts either (obj, x, y, w, h) or (obj, rectObject).
    static __SetRect(obj, x, y?, w?, h?) {
        local rect
        if !IsSet(y) {
            rect := x
            x := rect.X, y := rect.Y, w := rect.Width, h := rect.Height
        }
        ; Store the geometry as a single BoundingRect value property: OCR.Common's x/y/w/h getters read straight
        ; from it, so there is no need to (redundantly) define x/y/w/h as their own per-object value properties.
        obj.BoundingRect := {X: x, Y: y, Width: w, Height: h}
        return obj
    }

    ; Whether an options object carries a given named property. Accepts any object (IsObject), matching how
    ; Image.FromWindow detects an options object, so both paths honor the same argument consistently.
    static __HasOpt(obj, name) => IsObject(obj) && obj.HasProp(name)

    ; Returns obj.%name% if present, otherwise the supplied default.
    static __GetOpt(obj, name, default) => OCR.__HasOpt(obj, name) ? obj.%name% : default

    ;; ---------------------------------------------------------------------------------------------
    ;; Tesseract engine (the default OCR.Engine). All Tesseract/DllCall specifics live here so the
    ;; backend can be swapped by assigning OCR.Engine to any object with a matching Recognize().
    ;; ---------------------------------------------------------------------------------------------

    class TesseractEngine {
        Library := ""           ; full path/name of the Tesseract shared library ("" = auto-detect)
        DataPath := ""          ; tessdata folder ("" = derive from the library location / TESSDATA_PREFIX)
        SourceResolution := 70  ; DPI hint passed to Tesseract (only silences a resolution warning)

        __LibName := ""         ; resolved library path/name used as the DllCall prefix
        __LibHandle := 0        ; retained module handle (keeps the library pinned across calls)
        __DefaultDataPath := "" ; directory derived from the resolved library, used to find tessdata
        __ApiHandle := 0        ; cached, initialized TessBaseAPI handle reused across Recognize calls (0 = none)
        __ApiLang := ""         ; language the cached handle was initialized for
        __ApiData := ""         ; datapath the cached handle was initialized for

        __New(library := "", datapath := "") {
            if (library != "")
                this.Library := library
            if (datapath != "")
                this.DataPath := datapath
        }

        /**
         * Engine entry point. Recognizes text in an image and returns the raw lines/words geometry.
         * @param image A KS Image.
         * @param options {lang, datapath}.
         * @returns {Array} Array of lines, each an array of {Text, x, y, w, h} (image-pixel coords).
         */
        Recognize(image, options := 0) {
            local lang := "eng", datapath := this.DataPath, w := image.Width, h := image.Height, buf, handle, lines
            if IsObject(options) {
                if (options.HasProp("lang") && options.lang != "")
                    lang := options.lang
                if (options.HasProp("datapath") && options.datapath != "")
                    datapath := options.datapath
            }
            if (w < 1 || h < 1)
                throw ValueError("The image has no pixels to OCR.", -1)

            ; 8-bit grayscale, tightly packed and top-down: exactly what Tesseract wants for
            ; bytes_per_pixel=1. The buffer stays in scope until SetImage has copied it.
            buf := image.GetPixelData(1)
            handle := this.__GetApi(datapath, lang)   ; cached + already initialized for (lang, datapath)
            try {
                DllCall(this.__Sym("TessBaseAPISetImage"), "Ptr", handle, "Ptr", buf.Ptr, "Int", w, "Int", h, "Int", 1, "Int", w, "Cdecl")
                DllCall(this.__Sym("TessBaseAPISetSourceResolution"), "Ptr", handle, "Int", this.SourceResolution, "Cdecl")
                if (DllCall(this.__Sym("TessBaseAPIRecognize"), "Ptr", handle, "Ptr", 0, "Cdecl Int") != 0)
                    throw Error("Tesseract failed to recognize the image.", -1)
                lines := this.__BuildLines(handle)
            } finally {
                ; Release this image's pixels/results but KEEP the initialized engine for the next call:
                ; re-initializing per image (TessBaseAPIInit3 reloads the language model from disk) was the
                ; dominant per-call cost in a highlight->OCR loop.
                DllCall(this.__Sym("TessBaseAPIClear"), "Ptr", handle, "Cdecl")
            }
            return lines
        }

        ; Returns a Tesseract API handle initialized for (lang, datapath), creating it on first use and
        ; re-initializing only when the language or datapath changes. Retained across calls so the expensive
        ; language-model load (TessBaseAPIInit3) happens once instead of per image. The library itself is
        ; loaded and pinned by __EnsureLoaded.
        __GetApi(datapath, lang) {
            local handle
            this.__EnsureLoaded()
            if (this.__ApiHandle && this.__ApiLang == lang && this.__ApiData == datapath)
                return this.__ApiHandle
            this.__FreeApi()   ; tear down a handle initialized for a different language/datapath
            handle := DllCall(this.__Sym("TessBaseAPICreate"), "Cdecl Ptr")
            if !handle
                throw Error("Failed to create a Tesseract engine instance.", -1)
            ; __ApiHandle is only assigned after a successful init, so if __InitLanguage throws (e.g. missing
            ; traineddata) the freshly-created native handle would otherwise leak — the prior cached engine was
            ; already torn down by __FreeApi above. Release it here (End then Delete, mirroring __FreeApi) and
            ; re-raise, so a failed init never orphans a TessBaseAPI.
            try
                this.__InitLanguage(handle, datapath, lang)
            catch as e {
                try DllCall(this.__Sym("TessBaseAPIEnd"), "Ptr", handle, "Cdecl")
                try DllCall(this.__Sym("TessBaseAPIDelete"), "Ptr", handle, "Cdecl")
                throw e
            }
            this.__ApiHandle := handle, this.__ApiLang := lang, this.__ApiData := datapath
            return handle
        }

        ; Ends and deletes the cached engine handle, if any. Called when the language/datapath changes and
        ; from __Delete; safe to call when no handle exists.
        __FreeApi() {
            if (this.__ApiHandle) {
                try DllCall(this.__Sym("TessBaseAPIEnd"), "Ptr", this.__ApiHandle, "Cdecl")
                try DllCall(this.__Sym("TessBaseAPIDelete"), "Ptr", this.__ApiHandle, "Cdecl")
                this.__ApiHandle := 0, this.__ApiLang := "", this.__ApiData := ""
            }
        }

        ; Frees the retained Tesseract engine when this object is collected/destroyed.
        __Delete() => this.__FreeApi()

        ; Returns the Tesseract version string (also a quick way to confirm the library loaded).
        GetVersion() {
            this.__EnsureLoaded()
            local p := DllCall(this.__Sym("TessVersion"), "Cdecl Ptr")
            return p ? StrGet(p, "UTF-8") : ""
        }

        ; Returns an array of installed language codes (the tessdata\*.traineddata basenames), so a
        ; caller can discover what OCR.Language values are usable.
        GetAvailableLanguages() {
            this.__EnsureLoaded()
            local dir := this.__TessdataDir(), langs := []
            if (dir != "")
                Loop Files, dir "/" "*.traineddata"
                    langs.Push(StrReplace(A_LoopFileName, ".traineddata", ""))
            return langs
        }

        ; Resolves the tessdata directory (the first datapath candidate that actually holds *.traineddata).
        __TessdataDir() {
            local cands := [], d
            if (this.DataPath != "")
                cands.Push(this.DataPath)
            if (this.__DefaultDataPath != "") {
                cands.Push(this.__DefaultDataPath "/" "tessdata")
                cands.Push(this.__DefaultDataPath)
            }
            for d in cands
                if (d != "")
                    Loop Files, d "/" "*.traineddata"
                        return d   ; first candidate that contains at least one traineddata file
            return ""
        }

        ; Iterates the recognized page, grouping words (RIL_WORD = 3) into text lines (RIL_TEXTLINE = 2).
        ; Each word also carries Conf (0-100 recognition confidence). The iterator is deleted in a finally
        ; so it can't leak if a read throws.
        __BuildLines(handle) {
            local lines := [], curWords := "", pText, wordText, l, t, r, b, conf, pageIt
            local it := DllCall(this.__Sym("TessBaseAPIGetIterator"), "Ptr", handle, "Cdecl Ptr")
            if !it
                return lines
            try {
                pageIt := DllCall(this.__Sym("TessResultIteratorGetPageIterator"), "Ptr", it, "Cdecl Ptr")
                Loop {
                    if DllCall(this.__Sym("TessPageIteratorIsAtBeginningOf"), "Ptr", pageIt, "Int", 2, "Cdecl Int") {
                        curWords := []
                        lines.Push(curWords)
                    }
                    pText := DllCall(this.__Sym("TessResultIteratorGetUTF8Text"), "Ptr", it, "Int", 3, "Cdecl Ptr")
                    wordText := ""
                    if pText {
                        wordText := StrGet(pText, "UTF-8")
                        DllCall(this.__Sym("TessDeleteText"), "Ptr", pText, "Cdecl")
                    }
                    wordText := Trim(wordText, " `t`r`n")
                    if (wordText != "" && IsObject(curWords)) {
                        l := 0, t := 0, r := 0, b := 0
                        if DllCall(this.__Sym("TessPageIteratorBoundingBox"), "Ptr", pageIt, "Int", 3, "Int*", &l, "Int*", &t, "Int*", &r, "Int*", &b, "Cdecl Int") {
                            conf := DllCall(this.__Sym("TessResultIteratorConfidence"), "Ptr", it, "Int", 3, "Cdecl Float")
                            curWords.Push({Text: wordText, X: l, Y: t, Width: r - l, Height: b - t, Conf: Round(conf, 2)})
                        }
                    }
                    if !DllCall(this.__Sym("TessResultIteratorNext"), "Ptr", it, "Int", 3, "Cdecl Int")
                        break
                }
            } finally {
                DllCall(this.__Sym("TessResultIteratorDelete"), "Ptr", it, "Cdecl")
            }
            return lines
        }

        ; Builds the "lib/function" string for DllCall (DllCall accepts "/" as the separator on every platform).
        __Sym(func) => this.__LibName "/" func

        ; UTF-8-encodes a string into a NUL-terminated Buffer for passing to a char* API. Tesseract's
        ; datapath/language are UTF-8; DllCall's "AStr" would encode as ASCII (mangling any non-ASCII
        ; byte to '?'), so non-ASCII tessdata paths or language codes need this instead.
        __Utf8(s) {
            local b := Buffer(StrPut(s, "UTF-8"))   ; size includes the trailing NUL
            StrPut(s, b, "UTF-8")
            return b
        }

        ; Ensures the Tesseract library is loaded and pinned.
        __EnsureLoaded() {
            local cand, handle, ok
            if this.__LibHandle
                return this.__LibName
            for cand in this.__LibraryCandidates() {
                if (cand = "")
                    continue
                handle := this.__PlatformLoad(cand)
                if !handle
                    continue
                ok := false
                try ok := DllCall(cand "/" "TessVersion", "Cdecl Ptr") != 0
                if ok {
                    this.__LibHandle := handle
                    this.__LibName := cand
                    this.__DefaultDataPath := this.__DirOf(cand)
                    return cand
                }
                this.__PlatformFree(handle)
            }
            ; Plain Error (not OSError): the actionable message is the payload, and OSError would overwrite
            ; it on Windows with the formatted last-OS-error string ("The operation completed successfully.").
            throw Error("Could not load the Tesseract library. Install Tesseract OCR and, if needed, set OCR.Engine.Library to the full path of its shared library (e.g. 'C:\Program Files\Tesseract-OCR\libtesseract-5.dll').", -1)
        }

        ; Initializes the engine for a language, trying a few datapath candidates until one succeeds.
        ; datapath/language are passed as UTF-8 (Tesseract's char* encoding); the encoded buffers are
        ; held in locals so they stay alive across the DllCall.
        __InitLanguage(handle, datapath, lang) {
            local dp, ret, candidates := [], langBuf := this.__Utf8(lang), dpBuf
            if (datapath != "")
                candidates.Push(datapath)
            else if (this.__DefaultDataPath != "") {
                candidates.Push(this.__DefaultDataPath "/" "tessdata")
                candidates.Push(this.__DefaultDataPath)
            }
            candidates.Push("")   ; "" -> NULL: fall back to TESSDATA_PREFIX / built-in default
            for dp in candidates {
                if (dp = "")
                    ret := DllCall(this.__Sym("TessBaseAPIInit3"), "Ptr", handle, "Ptr", 0, "Ptr", langBuf.Ptr, "Cdecl Int")
                else {
                    dpBuf := this.__Utf8(dp)
                    ret := DllCall(this.__Sym("TessBaseAPIInit3"), "Ptr", handle, "Ptr", dpBuf.Ptr, "Ptr", langBuf.Ptr, "Cdecl Int")
                }
                if (ret = 0)
                    return
            }
            throw Error("Tesseract could not initialize language '" lang "'. Ensure '" lang ".traineddata' is installed (set OCR.Engine.DataPath to its tessdata folder, or OCR.Language to an installed language).", -1)
        }

        ; Returns the directory part of a path ("" if none).
        __DirOf(path) {
            local p := Max(InStr(path, "\", , -1), InStr(path, "/", , -1))
            return p ? SubStr(path, 1, p - 1) : ""
        }

#if WINDOWS
        __PlatformLoad(path) {
            ; LOAD_WITH_ALTERED_SEARCH_PATH (0x8) resolves sibling dependency DLLs (leptonica, etc.)
            ; from the library's own folder. The returned handle is retained for the process lifetime,
            ; keeping the module pinned (DllCall reloads+frees by name each call; the retained ref
            ; prevents an unload that would dangle the TessBaseAPI handle).
            local h := DllCall("kernel32/LoadLibraryExW", "WStr", path, "Ptr", 0, "UInt", 0x8, "Ptr")
            if !h
                h := DllCall("kernel32/LoadLibraryW", "WStr", path, "Ptr")
            return h
        }
        __PlatformFree(handle) => DllCall("kernel32/FreeLibrary", "Ptr", handle)

        __LibraryCandidates() {
            local c := []
            if (this.Library != "")
                c.Push(this.Library)
            c.Push(A_ProgramFiles "/Tesseract-OCR/libtesseract-5.dll")
            c.Push("C:/Program Files/Tesseract-OCR/libtesseract-5.dll")
            c.Push("C:/Program Files (x86)/Tesseract-OCR/libtesseract-5.dll")
            c.Push("libtesseract-5.dll", "libtesseract-5")
            return c
        }
#endif

#if !WINDOWS
        __PlatformLoad(path) => this.__Dlopen(path)
        __PlatformFree(handle) {
            local dl
            for dl in this.__DlLibs()
                try return DllCall(dl "/" "dlclose", "Ptr", handle, "Cdecl Int")
            return 0
        }
        __Dlopen(path) {
            local dl, h, pathBuf := this.__Utf8(path)
            ; dlopen takes a char* the loader treats as UTF-8, so pass UTF-8 bytes (via __Utf8) rather than
            ; "AStr": "AStr" would ANSI-encode the path, mangling any non-ASCII byte to '?' and breaking paths
            ; like /home/josé/lib/libtesseract.so. The buffer is held in a local so it stays alive across the call.
            ; RTLD_NOW (0x2) | RTLD_GLOBAL (0x100)
            for dl in this.__DlLibs() {
                try {
                    h := DllCall(dl "/" "dlopen", "Ptr", pathBuf.Ptr, "Int", 0x102, "Cdecl Ptr")
                    if h
                        return h
                }
            }
            return 0
        }
#endif

#if LINUX
        __DlLibs() => ["libdl.so.2", "libc.so.6", "libdl.so"]
        __LibraryCandidates() {
            local c := []
            if (this.Library != "")
                c.Push(this.Library)
            c.Push("libtesseract.so.5", "libtesseract.so.4", "libtesseract.so", "tesseract")
            return c
        }
#endif

#if OSX
        __DlLibs() => ["/usr/lib/libSystem.B.dylib"]
        __LibraryCandidates() {
            local c := []
            if (this.Library != "")
                c.Push(this.Library)
            c.Push("libtesseract.5.dylib", "libtesseract.dylib")
            c.Push("/opt/homebrew/lib/libtesseract.dylib", "/usr/local/lib/libtesseract.dylib")
            return c
        }
#endif
    }
}
