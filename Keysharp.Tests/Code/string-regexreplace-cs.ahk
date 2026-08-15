#NoTrayIcon

#import KS { * }
outputVarCount :=
match := RegExReplaceCs("abc123123", "123$", "xyz")

if (match == "abc123xyz")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

match := RegExReplaceCs("abc123", "i)^ABC")

if (match == "123")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

match := RegExReplaceCs("abcXYZ123", "abc(.*)123", "aaa$1zzz")

if (match == "aaaXYZzzz")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

match := RegExReplaceCs("abc123abc456", "abc\d+", "", &outputVarCount)

if (match == "")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (outputVarCount == 2)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"