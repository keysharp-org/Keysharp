#NoTrayIcon

#import KS { Base64Decode, Base64Encode }
#Include <assert>
b64 := "SGVsbG8sIHdvcmxkIQ==" ; "Hello, world!"
conv := Base64Decode(b64)
str2 := Base64Encode(conv)

Assert(b64 = str2, A_LineNumber)

; A string is taken as its UTF-8 bytes, so encoding the text produces what encoding those bytes does.
AssertEq(Base64Encode("Hello, world!"), b64, A_LineNumber)

; Another encoding can be named, such as the UTF-16 a Windows API works in.
AssertEq(Base64Encode("abc", "UTF-16"), "YQBiAGMA", A_LineNumber)

; A name which cannot be resolved is an error, never a silent substitution.
threw := false

try
	Base64Encode("abc", "no-such-encoding")
catch ValueError
	threw := true

Assert(threw, A_LineNumber)

FileAppend "pass", "*"
