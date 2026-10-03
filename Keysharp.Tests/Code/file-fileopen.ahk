#ErrorStdOut
#Warn All, StdOut
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

f.Close()
FileDelete(path)

f := FileOpen(path, "rw", "Unicode") ; Test text encoding. A new file gets a byte order mark.
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

AssertEq(enc, "UTF-8", A_LineNumber)

f.Close()

; Do not delete here, file is used for appending.
f := FileOpen(path, "a") ; Test append.
w := "testing"
count := f.Write(w)
pos := f.Pos
eof := f.AtEOF

AssertEq(eof, 1, A_LineNumber)  ; Appending leaves the pointer at the end of the file.

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
	f2 := FileOpen(handle, "h")
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

big := "" ; Longer than the read buffer, so a multi-byte character straddles a refill.

Loop 5000
	big .= "aö"

f := FileOpen(path, "w", "UTF-8-RAW")
f.Write(big)
f.Close()
f := FileOpen(path, "r", "UTF-8-RAW")
r := f.Read()
f.Pos := 0
line := f.ReadLine()
f.Close()

AssertEq(StrLen(r), 10000, A_LineNumber)

AssertEq(r, big, A_LineNumber)

AssertEq(line, big, A_LineNumber)

if (FileExist(path) != "")
	FileDelete(path)

HexOf(_Buffer)
{
	_Hex := ""
	Loop _Buffer.Size
		_Hex .= Format("{:02X}", NumGet(_Buffer, A_Index - 1, "UChar"))
	return _Hex
}

WriteBytesFile(_Name, _Values)
{
	if FileExist(_Name)
		FileDelete(_Name)
	_Bytes := Buffer(_Values.Length)
	for _Index, _Value in _Values
		NumPut("UChar", _Value, _Bytes, _Index - 1)
	FileAppend(_Bytes, _Name, "RAW")
}

try
{
	; Truncation writes one BOM, which also overrides the requested read encoding.
	FileAppend("old content", path)
	for Spec in [["UTF-8", "EFBBBF68C3A9"], ["UTF-16", "FFFE6800E900"]]
	{
		f := FileOpen(path, "w", Spec[1])
		f.Write("hé")
		f.Close()
		AssertEq(HexOf(FileRead(path, "RAW")), Spec[2], A_LineNumber)
	}
	f := FileOpen(path, "r", "UTF-8")
	AssertEq(f.Encoding, "UTF-16", A_LineNumber)
	AssertEq(f.Pos, 2, A_LineNumber)
	AssertEq(f.Read(), "hé", A_LineNumber)
	f.Close()

	; Encoding changes affect both directions; no BOM means no skipped bytes.
	FileDelete(path)
	f := FileOpen(path, "rw", "UTF-8-RAW")
	f.Encoding := "UTF-16-RAW"
	AssertEq(f.Encoding, "UTF-16", A_LineNumber)
	AssertEq(f.Write("hé"), 4, A_LineNumber)
	f.Pos := 0
	AssertEq(f.Read(), "hé", A_LineNumber)
	f.Close()
	f := FileOpen(path, "r", "UTF-16")
	AssertEq(f.Pos, 0, A_LineNumber)
	AssertEq(f.Read(), "hé", A_LineNumber)
	f.Close()

	; ReadLine accepts all line endings; UTF-8 reads keep a surrogate pair whole.
	emoji := Chr(0x1F600)
	f := FileOpen(path, "w", "UTF-8-RAW")
	f.Write("a" emoji "b`r`nsecond`rthird`nlast")
	f.Close()
	f := FileOpen(path, "r", "UTF-8-RAW")
	for Expected in ["a" emoji "b", "second", "third", "last", ""]
		AssertEq(f.ReadLine(), Expected, A_LineNumber)
	AssertEq(f.AtEOF, 1, A_LineNumber)
	f.Pos := 0
	AssertEq(f.Read(2), "a", A_LineNumber)
	AssertEq(f.Read(1), emoji, A_LineNumber)
	AssertEq(f.Pos, 5, A_LineNumber)
	f.Close()
	f := FileOpen(path, "r`n`r")
	AssertEq(f.Read(), "a" emoji "b`nsecond`nthird`nlast", A_LineNumber)
	f.Close()
	f := FileOpen(path, "w`n", "UTF-8-RAW")
	f.WriteLine("x")
	f.Write("y`r`nz`n")
	f.Close()
	AssertEq(HexOf(FileRead(path, "RAW")), "780D0A790D0A7A0D0A", A_LineNumber)

	; Numeric EOF is empty, Char is signed, and Seek does not truncate its offset.
	f := FileOpen(path, "w", "UTF-8-RAW")
	AssertEq(f.WriteChar(-1), 1, A_LineNumber)
	AssertEq(f.Seek(0x100000000), 1, A_LineNumber)
	AssertEq(f.Pos, 0x100000000, A_LineNumber)
	f.Close()
	f := FileOpen(path, "r", "UTF-8-RAW")
	AssertEq(f.ReadChar(), -1, A_LineNumber)
	AssertEq(f.ReadInt(), "", A_LineNumber)
	f.Close()
	f := FileOpen(path, "w", "UTF-8-RAW")
	AssertEq(f.RawWrite("ab"), 4, A_LineNumber)
	f.Close()
	AssertEq(HexOf(FileRead(path, "RAW")), "61006200", A_LineNumber)

	; Reading text leaves raw reads and writes at the logical byte position.
	WriteBytesFile(path, [111, 110, 101, 10, 116, 119, 111, 10])
	f := FileOpen(path, "rw", "UTF-8-RAW")
	AssertEq(f.ReadLine(), "one", A_LineNumber)
	AssertEq(f.Pos, 4, A_LineNumber)
	AssertEq(f.ReadUChar(), Ord("t"), A_LineNumber)
	f.Write("X")
	f.Close()
	AssertEq(FileRead(path, "UTF-8-RAW"), "one`ntXo`n", A_LineNumber)

	; A multibyte character across the refill stays wholly unread until returned.
	filler := ""
	Loop 8191
		filler .= "a"
	for Spec in [["UTF-8-RAW", "é", "C3A9"], ["CP932", Chr(0x3042), "82A0"]]
	{
		f := FileOpen(path, "w", Spec[1])
		f.Write(filler Spec[2] "Z")
		f.Close()
		f := FileOpen(path, "r", Spec[1])
		AssertEq(f.Read(8191), filler, A_LineNumber)
		AssertEq(f.Pos, 8191, A_LineNumber)
		Raw := Buffer(2)
		AssertEq(f.RawRead(Raw), 2, A_LineNumber)
		AssertEq(HexOf(Raw), Spec[3], A_LineNumber)
		f.Pos := 8191
		AssertEq(f.Read(1), Spec[2], A_LineNumber)
		AssertEq(f.Pos, 8193, A_LineNumber)
		f.Close()
	}

	; Memory-backed files expose writes immediately and read external edits.
	mem := Buffer(8, 0)
	mf := File(mem)
	mf.WriteInt(5)
	AssertEq(NumGet(mem, "Int"), 5, A_LineNumber)
	for Index, Value in [97, 98, 10, 99, 100]
		NumPut("UChar", Value, mem, Index - 1)
	mf.Pos := 0
	AssertEq(mf.ReadLine(), "ab", A_LineNumber)
	NumPut("UChar", 122, mem, 3)
	AssertEq(mf.Read(2), "zd", A_LineNumber)
	mf.Close()

	; Shift bytes belong to the next character; committed state survives a line boundary.
	WriteBytesFile(path, [97, 97, 27, 36, 66, 36, 34, 10, 36, 36, 10])
	f := FileOpen(path, "r", "CP50220")
	AssertEq(f.Read(2), "aa", A_LineNumber)
	AssertEq(f.Pos, 2, A_LineNumber)
	AssertEq(f.ReadUChar(), 27, A_LineNumber)
	f.Pos := 2
	AssertEq(f.ReadLine(), Chr(0x3042), A_LineNumber)
	AssertEq(f.ReadLine(), Chr(0x3044), A_LineNumber)
	f.Close()

	; Metadata and mutable memory preserve committed shift state without replaying old bytes.
	WriteBytesFile(path, [126, 123, 86, 80, 13, 86, 80, 13, 86, 80, 126, 125])
	f := FileOpen(path, "rw", "CP52936")
	AssertEq(f.ReadLine(), Chr(0x4E2D), A_LineNumber)
	Handle := f.Handle
	AssertEq(f.ReadLine(), Chr(0x4E2D), A_LineNumber)
	f.Length := f.Length
	AssertEq(f.ReadLine(), Chr(0x4E2D), A_LineNumber)
	f.Close()
	Shifted := FileRead(path, "RAW")
	mf := File(Shifted, "CP52936")
	AssertEq(mf.ReadLine(), Chr(0x4E2D), A_LineNumber)
	NumPut("UChar", 65, Shifted, 0)
	AssertEq(mf.ReadLine(), Chr(0x4E2D), A_LineNumber)
	mf.Close()

	; Exporting a handle after an orphan CR must leave the next byte available to a write.
	WriteBytesFile(path, [65, 13, 66, 10])
	f := FileOpen(path, "rw", "CP932")
	AssertEq(f.ReadLine(), "A", A_LineNumber)
	Handle := f.Handle
	AssertEq(f.Pos, 2, A_LineNumber)
	f.Write("X")
	f.Close()
	AssertEq(FileRead(path, "CP932"), "A`rX`n", A_LineNumber)

	; Invalid UTF-8 must not consume the following fallback byte.
	WriteBytesFile(path, [0xF4, 0xBF])
	f := FileOpen(path, "r", "UTF-8-RAW")
	AssertEq(f.Read(1), Chr(0xFFFD), A_LineNumber)
	AssertEq(f.Pos, 1, A_LineNumber)
	AssertEq(f.ReadUChar(), 0xBF, A_LineNumber)
	f.Close()

	; A first complete stateful group can exceed Read's requested count.
	WriteBytesFile(path, [0xB3, 65, 66])
	f := FileOpen(path, "r", "CP57002")
	AssertEq(f.Read(1), Chr(0x915) "A", A_LineNumber)
	AssertEq(f.Pos, 2, A_LineNumber)
	AssertEq(f.ReadUChar(), 66, A_LineNumber)
	f.Close()

	; An invalid UTF-16BE high surrogate must not consume a pair across the byte buffer.
	Values := []
	Loop 4094
		Values.Push(0, 97)
	Values.Push(0xD8, 0, 0xD8, 0x3D, 0xDE, 0, 0, 10)
	WriteBytesFile(path, Values)
	f := FileOpen(path, "r", "CP1201")
	AssertEq(f.ReadLine(), SubStr(filler, 1, 4094) Chr(0xFFFD) emoji, A_LineNumber)
	AssertEq(f.Pos, 8196, A_LineNumber)
	f.Close()
}
finally
{
	f.Close()
	if IsSet(mf)
		mf.Close()
	if FileExist(path)
		FileDelete(path)
}

FileAppend "pass", "*"
