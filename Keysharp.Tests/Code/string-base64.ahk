#NoTrayIcon

#import KS { Base64Decode, Base64Encode }
b64 := "SGVsbG8sIHdvcmxkIQ==" ; "Hello, world!"
conv := Base64Decode(b64)
str2 := Base64Encode(conv)

if (b64 = str2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"