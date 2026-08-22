#NoTrayIcon

#import KS { HashMap }
#Include <assert>
a := HashMap() ; Map with a key and property each with the same name.
a["test"] := 3
a.test := 2

AssertEq(a["test"], 3, A_LineNumber)

; HashMap should be unsorted, usually in insertion order (although this is an implementation detail)
m := HashMap(1.0, "double", 1, "integer", "1", "string", {}, "object")
i := 0
for k, v in m {
	i++
	AssertEq(v, ["double", "integer", "string", "object"][i], A_LineNumber)
}

FileAppend "pass", "*"
