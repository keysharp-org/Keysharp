#NoTrayIcon

#import KS { Base64Decode, Base64Encode }
b64 := "SGVsbG8sIHdvcmxkIQ==" ; "Hello, world!"
conv := Base64Decode(b64)
str2 := Base64Encode(conv)

if (b64 = str2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
; A string is taken as its UTF-8 bytes, so encoding the text produces what encoding those bytes does.
if (Base64Encode("Hello, world!") == b64)
	FileAppend "pass", "*"

; Another encoding can be named, such as the UTF-16 a Windows API works in.
if (Base64Encode("abc", "UTF-16") == "YQBiAGMA")
	FileAppend "pass", "*"

; A name which cannot be resolved is an error, never a silent substitution.
try
	Base64Encode("abc", "no-such-encoding")
catch ValueError
	FileAppend "pass", "*"
