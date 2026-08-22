#NoTrayIcon
#Include <assert>

path := "./testfileobject1.txt"

if (FileExist(path) != "")
	FileDelete(path)

f := FileOpen(path, "rw") ; Simplest first, read/write.
w := "testing"
count := f.WriteLine(w)
f.Seek(0) ; Test seeking from beginning.
r := f.ReadLine()

AssertEq(r, "testing", A_LineNumber)

AssertEq(count, 8, A_LineNumber)  ; Add one for the newline.

f.Close()

if (FileExist(path) != "")
	FileDelete(path)

f := FileOpen(path, "rw") ; Read/write integers.
val := 0x01020304
count := f.WriteUInt(val)
f.Seek(0)
r := f.ReadUInt()

AssertEq(val, r, A_LineNumber)

AssertEq(count, 4, A_LineNumber)

val2 := -12345678
count := f.WriteInt(val2)
f.Seek(-4, 1) ; Test seeking from current.
r2 := f.ReadInt()

AssertEq(val2, r2, A_LineNumber)

AssertEq(count, 4, A_LineNumber)

f.Close()

if (FileExist(path) != "")
	FileDelete(path)

f := FileOpen(path, "rw") ; Read/write buffers and arrays.
buf := Buffer(4, 9)
count := f.RawWrite(buf)
f.Seek(0)
buf2 := Buffer(4, 0)
f.RawRead(buf2)

Loop (buf.Size)
{
	p1 := buf[A_Index]
	p2 := buf2[A_Index]

	AssertEq(p1, p2, A_LineNumber)
}

f.Seek(0)
arr := Array()

Loop (buf.Size)
{
	arr.Push(A_Index)
}

f.RawRead(arr)

Loop (buf.Size)
{
	p1 := arr[A_Index]
	p2 := buf2[A_Index]

	AssertEq(p1, p2, A_LineNumber)
}

f.Close()

if (FileExist(path) != "")
	FileDelete(path)

f := FileOpen(path, "rw", "Unicode") ; Test text encoding.
w := "testing"
count := f.Write(w)
f.Seek(2) ; A unicode file will have a 2 byte long byte order mark.
r := f.ReadLine()

AssertEq(r, "testing", A_LineNumber)

AssertEq(count, 14, A_LineNumber)  ; Unicode is two bytes per char.

AssertEq(f.Length, 16, A_LineNumber)  ; BOM plus 2 bytes per char.

f.Close()

f := FileOpen(path, "rw", "Unicode") ; Ensure reading an existing file with a BOM works.
w := "testing"
r := f.ReadLine()

AssertEq(r, "testing", A_LineNumber)

AssertEq(w.Length, r.Length, A_LineNumber)

f.Close()

if (FileExist(path) != "")
	FileDelete(path)

A_FileEncoding := "utf-8-raw"
f := FileOpen(path, "rw") ; Test position.
w := "testing"
count := f.Write(w)
pos := f.Pos
len := StrLen(w)

AssertEq(len, pos, A_LineNumber)

eof := f.AtEOF

AssertEq(eof, 1, A_LineNumber)

len := f.Length

AssertEq(len, 7, A_LineNumber)

enc := f.Encoding

AssertEq(enc, "utf-8", A_LineNumber)

f.Close()

; Do not delete here, file is used for appending.
f := FileOpen(path, "a") ; Test append.
w := "testing"
count := f.Write(w)
pos := f.Pos
eof := f.AtEOF

AssertEq(eof, 0, A_LineNumber)  ; With append mode, you're never really at the "end" of the file.

len := f.Length

AssertEq(pos, 14, A_LineNumber)

AssertEq(len, 14, A_LineNumber)

f.Close()

if (FileExist(path) != "")
	FileDelete(path)

f := FileOpen(path, "w") ; Test write only.
w := "testing"
count := f.Write(w)
f.Close()

f := FileOpen(path, "w") ; Test write only on an existing file, which should clear it.
pos := f.Pos
eof := f.AtEOF
len := f.Length

AssertEq(eof, 1, A_LineNumber)  ; Overwrite should cause it to be an empty file.

AssertEq(pos, 0, A_LineNumber)

AssertEq(len, 0, A_LineNumber)

f.Close()

if (FileExist(path) != "")
	FileDelete(path)

f := FileOpen(path, "w") ; Test write only.
w := "testing"
count := f.Write(w)
f.Close()

f := FileOpen(path, "rw") ; Test read/write on an existing file, which should not clear it.
pos := f.Pos
eof := f.AtEOF
len := f.Length

AssertEq(eof, 0, A_LineNumber)  ; At position zero, so not at EOF.

AssertEq(pos, 0, A_LineNumber)

AssertEq(len, 7, A_LineNumber)

f.Close()

b := false
#if WINDOWS
fShareRead := ""
try
{
	fShareRead := FileOpen(path, "r -r")
	FileOpen(path, "r")
}
catch
{
	b := true
}
try
{
	fShareRead.Close()
}
catch
{
}

AssertEq(b, true, A_LineNumber)

b := false
fShareWrite := ""

try
{
	fShareWrite := FileOpen(path, "rw -w")
	FileOpen(path, "rw")
}
catch
{
	b := true
}
try
{
	fShareWrite.Close()
}
catch
{
}

AssertEq(b, true, A_LineNumber)

b := false
fNumLock1 := ""
fNumLock2 := ""

try
{
	fNumLock1 := FileOpen(path, 0) ; Numeric flags without share bits should lock.
	fNumLock2 := FileOpen(path, 0)
}
catch
{
	b := true
}
try
{
	fNumLock1.Close()
}
catch
{
}
try
{
	fNumLock2.Close()
}
catch
{
}

AssertEq(b, true, A_LineNumber)
#endif

b := false

try
{
	f := FileOpen(path, "r -r")
	handle := f.Handle
	f2 := FileOpen(handle, "r h")
	f2.Close()
	f.Close()
}
catch
{
	b := true
}

Assert(!(b == true), A_LineNumber)

if (FileExist(path) != "")
	FileDelete(path)

b := false

try
{
	FileOpen(path, "r")
}
catch
{
	b := true
}

AssertEq(b, true, A_LineNumber)

if (FileExist(path) != "")
	FileDelete(path)

f := FileOpen(path, "rw", "UTF-8-RAW") ; The character count of Read() is optional.
f.Write("hello wörld")
f.Seek(0)
r := f.Read(5)

AssertEq(r, "hello", A_LineNumber)

r := f.Read(0) ; An explicit zero reads nothing; only an omitted count means "the rest".

AssertEq(r, "", A_LineNumber)

r := f.Read() ; Omitted: everything left from the current position, multi-byte characters included.

AssertEq(r, " wörld", A_LineNumber)

AssertEq(f.AtEOF, 1, A_LineNumber)

b := false

try
	f.Read(-1)
catch
	b := true

AssertEq(b, true, A_LineNumber)

f.Close()

if (FileExist(path) != "")
	FileDelete(path)

big := "" ; Longer than one decode chunk, so a multi-byte character straddles a chunk boundary.

Loop 2500
	big .= "aö"

f := FileOpen(path, "w", "UTF-8-RAW")
f.Write(big)
f.Close()
f := FileOpen(path, "r", "UTF-8-RAW")
r := f.Read()
f.Close()

AssertEq(StrLen(r), 5000, A_LineNumber)

AssertEq(r, big, A_LineNumber)

if (FileExist(path) != "")
	FileDelete(path)

FileAppend "pass", "*"
