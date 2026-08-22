#NoTrayIcon

#Requires AutoHotkey v2.1
#Include <assert>

struct AB1 {
    #StructPack 1
    a : Int8
    b : Int32
}

struct AB2 {
    a : Int8
    b : Int32
}

; #StructPack 1 disables padding, so AB1 is 1 + 4 = 5 bytes.
ab := AB1()
Assert(!(ab.Size != 5), A_LineNumber)

; Without #StructPack, b is aligned to offset 4, so AB2 is 8 bytes.
Assert(!(AB2().Size != 8), A_LineNumber)

; ObjGetDataPtr / ObjGetDataSize mirror the struct's Ptr / Size.
Assert(!(ObjGetDataPtr(ab) != ab.Ptr), A_LineNumber)

Assert(!(ObjGetDataSize(ab) != 5), A_LineNumber)

; Since alpha.27, only a boxed pointer created by Struct.At can be rebound.
buf1 := Buffer(8, 0)
buf2 := Buffer(8, 0)
pt := AB1.At(buf1.Ptr)
ObjSetDataPtr(pt, buf2.Ptr)
Assert(!(pt.Ptr != buf2.Ptr), A_LineNumber)

try ObjSetDataPtr(AB1(), buf1.Ptr)
catch Error
    normalStructFailed := true

Assert(!(!IsSet(normalStructFailed)), A_LineNumber)

FileAppend "pass", "*"
