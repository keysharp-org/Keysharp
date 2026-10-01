#ErrorStdOut
#Warn All, StdOut
#Include <assert>

; Each option is read once, in order, so an invalid one is reported before the image is loaded.
for option in ["*Dir", "*Dir1", "*Dir9", "*DirTop", "*DirTopLeftX", "*DirTopLeft *Dir2"] {
    try {
        ImageSearch(, , 0, 0, 0, 0, option " missing-image.png")
        Assert(false, A_LineNumber)
    } catch ValueError as caught {
        Assert(InStr(caught.Message, "*Dir"), A_LineNumber)
    }
}

Throws(() => ImageSearch(, , 0, 0, 0, 0, "*TransNotAColor missing-image.png"), A_LineNumber, ValueError)

; Direction names in any case, and colors by name or hexadecimal, reach the image load.
for option in ["*DirTopLeft", "*dirbottomright", "*w8 *h8 *DirCenter", "*TransFFFFAA *Trans0xFFFFAA *TransBlack *30 *DirLeftTop"] {
    try {
        ImageSearch(, , 0, 0, 0, 0, option " missing-image.png")
        Assert(false, A_LineNumber)
    } catch ValueError as caught {
        Assert(InStr(caught.Message, "missing-image.png"), A_LineNumber)
    }
}

; An image that cannot be loaded raises one error.
raised := 0
CountError(*) {
    global raised
    raised++
    return -1
}
OnError(CountError)
ImageSearch(, , 0, 0, 0, 0, "missing-image.png")
OnError(CountError, 0)
AssertEq(raised, 1, A_LineNumber)

FileAppend "pass", "*"
