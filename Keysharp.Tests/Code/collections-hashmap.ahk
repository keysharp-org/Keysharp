#NoTrayIcon

#import KS { HashMap }
a := HashMap() ; Map with a key and property each with the same name.
a["test"] := 3
a.test := 2

if (a["test"] == 3)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

; HashMap should be unsorted, usually in insertion order (although this is an implementation detail)
m := HashMap(1.0, "double", 1, "integer", "1", "string", {}, "object")
i := 0
for k, v in m {
	i++
	if (i == 1 && v == "double")
		FileAppend "pass", "*"
	else if (i == 2 && v == "integer")
		FileAppend "pass", "*"
	else if (i == 3 && v == "string")
		FileAppend "pass", "*"
	else if (i == 4 && v == "object")
		FileAppend "pass", "*"
	else
		FileAppend "fail", "*"
}