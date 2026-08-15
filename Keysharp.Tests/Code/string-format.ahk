#NoTrayIcon

#import KS { FormatCs }
; Test 1: Basic number formatting.
s := Format("{1}", 123)
if (s == "123")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; Test 2: Zero padding with field width.
s := Format("{1:08d}", 123)
if (s == "00000123")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; Test 3: Plus sign flag for positive numbers.
s := Format("{1:+d}", 123)
if (s == "+123")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; Test 4: Plus sign flag with a negative number.
s := Format("{1:+d}", -123)
if (s == "-123")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; Test 5: Hexadecimal conversion (lower-case).
s := Format("{1:x}", 255)
if (s == "ff")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; Test 6: Alternate hexadecimal with prefix.
s := Format("{1:#x}", 255)
if (s == "0xff")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; Test 7: String conversion.
s := Format("{1}", "Hello")
if (s == "Hello")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; Test 8: Literal braces.
s := Format("{{}}")
if (s == "{}")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; Test 9: Omitted index (using next input values).
s := Format("{} {}", 1, 2)
if (s == "1 2")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; Test 10: Custom uppercase transformation.
s := Format("{1:U}", "test")
if (s == "TEST")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; Test 11: Title case transformation.
s := Format("{1:T}", "hello world")
if (s == "Hello World")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; Test 12: Left alignment (padding to 10 characters).
s := Format("{1:-10}", 123)
if (s == "123       ")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; Test 13: Floating�point formatting with precision.
s := Format("{1:.2f}", 1.2345)
if (s == "1.23")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; Test 14: String precision (maximum number of characters).
s := Format("{1:.3s}", "abcdef")
if (s == "abc")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; Test 15: Signed hexadecimal double-precision floating-point value.
s := Format("{:a}", 255)
if (s == "0x1.fe00000000000p+7")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

s := Format("{:A}", 255)
if (s == "0X1.FE00000000000P+7")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

; Test 16: Memory address in hexadecimal digits.
s := Format("{:p}", 255)
if (s == "00000000000000FF")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"

s := FormatCs("{1}", 123)

; Test 15: Signed hexadecimal double-precision floating-point value.
s := Format("{:a}", 255)
if (s == "0x1.fe00000000000p+7")
    FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
s := FormatCs("{1}", 123.456)

; Test 16: Memory address in hexadecimal digits.
s := Format("{:p}", 255)
if (s == "00000000000000FF")
    FileAppend "pass", "*"
else
    FileAppend "fail", "*"