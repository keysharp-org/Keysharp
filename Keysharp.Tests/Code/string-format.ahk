#NoTrayIcon

#import KS { FormatCs }
#Include <assert>
; Test 1: Basic number formatting.
s := Format("{1}", 123)
AssertEq(s, "123", A_LineNumber)

; Test 2: Zero padding with field width.
s := Format("{1:08d}", 123)
AssertEq(s, "00000123", A_LineNumber)

; Test 3: Plus sign flag for positive numbers.
s := Format("{1:+d}", 123)
AssertEq(s, "+123", A_LineNumber)

; Test 4: Plus sign flag with a negative number.
s := Format("{1:+d}", -123)
AssertEq(s, "-123", A_LineNumber)

; Test 5: Hexadecimal conversion (lower-case).
s := Format("{1:x}", 255)
AssertEq(s, "ff", A_LineNumber)

; Test 6: Alternate hexadecimal with prefix.
s := Format("{1:#x}", 255)
AssertEq(s, "0xff", A_LineNumber)

; Test 7: String conversion.
s := Format("{1}", "Hello")
AssertEq(s, "Hello", A_LineNumber)

; Test 8: Literal braces.
s := Format("{{}}")
AssertEq(s, "{}", A_LineNumber)

; Test 9: Omitted index (using next input values).
s := Format("{} {}", 1, 2)
AssertEq(s, "1 2", A_LineNumber)

; Test 10: Custom uppercase transformation.
s := Format("{1:U}", "test")
AssertEq(s, "TEST", A_LineNumber)

; Test 11: Title case transformation.
s := Format("{1:T}", "hello world")
AssertEq(s, "Hello World", A_LineNumber)

; Test 12: Left alignment (padding to 10 characters).
s := Format("{1:-10}", 123)
AssertEq(s, "123       ", A_LineNumber)

; Test 13: Floating�point formatting with precision.
s := Format("{1:.2f}", 1.2345)
AssertEq(s, "1.23", A_LineNumber)

; Test 14: String precision (maximum number of characters).
s := Format("{1:.3s}", "abcdef")
AssertEq(s, "abc", A_LineNumber)

; Test 15: Signed hexadecimal double-precision floating-point value.
s := Format("{:a}", 255)
AssertEq(s, "0x1.fe00000000000p+7", A_LineNumber)

s := Format("{:A}", 255)
AssertEq(s, "0X1.FE00000000000P+7", A_LineNumber)

; Test 16: Memory address in hexadecimal digits.
s := Format("{:p}", 255)
AssertEq(s, "00000000000000FF", A_LineNumber)

s := FormatCs("{1}", 123)

; Test 15: Signed hexadecimal double-precision floating-point value.
s := Format("{:a}", 255)
AssertEq(s, "0x1.fe00000000000p+7", A_LineNumber)
	
s := FormatCs("{1}", 123.456)

; Test 16: Memory address in hexadecimal digits.
s := Format("{:p}", 255)
AssertEq(s, "00000000000000FF", A_LineNumber)

FileAppend "pass", "*"
