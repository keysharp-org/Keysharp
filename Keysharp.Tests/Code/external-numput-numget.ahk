#NoTrayIcon
#Include <assert>

buf := Buffer(100, 0)
ret := NumPut("int", 1, buf)
b1 := NumGet(buf, 0, "int")

AssertEq(b1, 1, A_LineNumber)

AssertEq(buf.Ptr + 4, ret, A_LineNumber)

ret := NumPut("int", -1, buf)
b1 := NumGet(buf, 0, "int")

AssertEq(b1, -1, A_LineNumber)
	
AssertEq(buf.Ptr + 4, ret, A_LineNumber)

ret := NumPut("uint", 0xFFFFFFFF, buf)
b1 := NumGet(buf, 0, "uint")

AssertEq(b1, 0xFFFFFFFF, A_LineNumber)
	
AssertEq(buf.Ptr + 4, ret, A_LineNumber)

ret := NumPut("char", 123, buf)
b1 := NumGet(buf, 0, "char")

AssertEq(b1, 123, A_LineNumber)

AssertEq(buf.Ptr + 1, ret, A_LineNumber)

ret := NumPut("char", -123, buf)
b1 := NumGet(buf, 0, "char")

AssertEq(b1, -123, A_LineNumber)

AssertEq(buf.Ptr + 1, ret, A_LineNumber)

ret := NumPut("uchar", 255, buf)
b1 := NumGet(buf, 0, "uchar")

AssertEq(b1, 255, A_LineNumber)
	
AssertEq(buf.Ptr + 1, ret, A_LineNumber)

ret := NumPut("uchar", 256, buf)
b1 := NumGet(buf, 0, "uchar")

AssertEq(b1, 0, A_LineNumber)

AssertEq(buf.Ptr + 1, ret, A_LineNumber)

ret := NumPut("short", 10000, buf)
b1 := NumGet(buf, 0, "short")

AssertEq(b1, 10000, A_LineNumber)
	
AssertEq(buf.Ptr + 2, ret, A_LineNumber)

ret := NumPut("short", -10000, buf)
b1 := NumGet(buf, 0, "short")

AssertEq(b1, -10000, A_LineNumber)
	
AssertEq(buf.Ptr + 2, ret, A_LineNumber)

ret := NumPut("ushort", 50000, buf)
b1 := NumGet(buf, 0, "ushort")

AssertEq(b1, 50000, A_LineNumber)
	
AssertEq(buf.Ptr + 2, ret, A_LineNumber)

ret := NumPut("ushort", 65536, buf)
b1 := NumGet(buf, 0, "ushort")

AssertEq(b1, 0, A_LineNumber)
	
AssertEq(buf.Ptr + 2, ret, A_LineNumber)

ret := NumPut("int64", 0xFFFFFFFFFFFFFFFF, buf)
b1 := NumGet(buf, 0, "int64")

AssertEq(b1, -1, A_LineNumber)
	
AssertEq(buf.Ptr + 8, ret, A_LineNumber)

ret := NumPut("double", 1.2345, buf)
b1 := NumGet(buf, 0, "double")

AssertEq(b1, 1.2345, A_LineNumber)
	
AssertEq(buf.Ptr + 8, ret, A_LineNumber)

ret := NumPut("double", -1.2345, buf)
b1 := NumGet(buf, 0, "double")

AssertEq(b1, -1.2345, A_LineNumber)
	
AssertEq(buf.Ptr + 8, ret, A_LineNumber)

ret := NumPut("float", 1.2345, buf)
b1 := NumGet(buf, 0, "float")

AssertEq(b1, 1.2345, A_LineNumber)
	
AssertEq(buf.Ptr + 4, ret, A_LineNumber)

ret :=NumPut("float", -1.2345, buf)
b1 := NumGet(buf, 0, "float")

AssertEq(b1, -1.2345, A_LineNumber)
	
AssertEq(buf.Ptr + 4, ret, A_LineNumber)

NumPut("char", 1, buf)
NumPut("char", 2, buf, 1)
NumPut("char", 3, buf, 2)
NumPut("char", 4, buf, 3)

b1 := NumGet(buf, 0, "uint")

AssertEq(b1, 0x04030201, A_LineNumber)
	
AssertEq(buf.Ptr + 4, ret, A_LineNumber)

ret := NumPut("int", 1234, "short", 10000, "double", 1.2345, buf)
b1 := NumGet(buf, 0, "int")

AssertEq(b1, 1234, A_LineNumber)
	
AssertEq(buf.Ptr + 14, ret, A_LineNumber)

b1 := NumGet(buf, 4, "short")

AssertEq(b1, 10000, A_LineNumber)

b1 := NumGet(buf, 6, "double")

AssertEq(b1, 1.2345, A_LineNumber)

ret := NumPut("int", 0, buf)

AssertEq(buf.Ptr + 4, ret, A_LineNumber)

ret := NumPut("int", 0x01020304, buf, 2)
b1 := NumGet(buf, 0, "int")

AssertEq(b1, 0x03040000, A_LineNumber)
	
AssertEq(buf.Ptr + 6, ret, A_LineNumber)

buf := Buffer(4, 0)
ret := NumPut("int", 123, buf)
val := false

AssertEq(buf.Ptr + 4, ret, A_LineNumber)

try
{
	NumPut("int", 123, buf, 2)
}
catch
{
	val := true
}

AssertEq(val, true, A_LineNumber)

buf := Buffer(100, 0)
ret := NumPut("ptr", buf.Ptr, buf)

b1 := NumGet(buf, 0, "ptr")

AssertEq(b1, buf.Ptr, A_LineNumber)

b1 := NumGet(buf, 0, "uptr")

AssertEq(b1, buf.Ptr, A_LineNumber)

b1 := NumGet(buf, 0, "int64")

AssertEq(b1, buf.Ptr, A_LineNumber)

NumPut("uint", 5000000000, buf)
ret := NumGet(buf, "uint")

AssertEq(ret, 705032704, A_LineNumber)

; A type which does not name a number is rejected rather than silently reading or writing the wrong bytes.
Throws(() => NumGet(buf, 0, "bogus"), A_LineNumber, ValueError)
Throws(() => NumGet(buf, 0, ""), A_LineNumber, ValueError)
Throws(() => NumGet(buf, 0, "str"), A_LineNumber, ValueError)
Throws(() => NumPut("bogus", 1, buf), A_LineNumber, ValueError)
Throws(() => NumPut("str", 1, buf), A_LineNumber, ValueError)

; Pairs are written as they are read, so the ones before a rejected type have already landed.
NumPut("int64", 0, buf)
try NumPut("int", 1, "bogus", 2, buf)
AssertEq(NumGet(buf, 0, "int64"), 1, A_LineNumber)

; A value which does not name a number is rejected rather than written as a zero.
Throws(() => NumPut("int", "abc", buf), A_LineNumber, ValueError)
Throws(() => NumPut("int", {}, buf), A_LineNumber, ValueError)
Throws(() => NumPut("double", "abc", buf), A_LineNumber, ValueError)
; ...but a numeric string still is one, and a pointer type still takes an object carrying an address.
NumPut("int64", 0, buf)
NumPut("int", "42", buf)
AssertEq(NumGet(buf, 0, "int"), 42, A_LineNumber)
NumPut("ptr", buf, buf)
AssertEq(NumGet(buf, 0, "ptr"), buf.Ptr, A_LineNumber)

; An offset far past the buffer is caught. It used to be truncated to 32 bits, which came back negative
; and slipped through the bounds check into memory 2 GB away.
Throws(() => NumPut("int", 1, buf, 0x80000000), A_LineNumber, IndexError)
Throws(() => NumGet(buf, 0x80000000, "int"), A_LineNumber, IndexError)
Throws(() => NumPut("int", 1, buf, 0x7FFFFFFFFFFF), A_LineNumber, IndexError)

; The first 64KB of the address space is never mapped, so an address in it is a mistake rather than
; something to dereference.
Throws(() => NumGet(4, "int"), A_LineNumber, IndexError)
Throws(() => NumPut("int", 1, 4), A_LineNumber, IndexError)

FileAppend "pass", "*"
