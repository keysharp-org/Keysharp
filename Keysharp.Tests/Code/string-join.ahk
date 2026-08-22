#NoTrayIcon

#import KS { Join }
#Include <assert>
str := Join(",", "1", "2", "3")

AssertEq(str, "1,2,3", A_LineNumber)

str := Join(",", 1, 2, 3)

AssertEq(str, "1,2,3", A_LineNumber)

arr := [10, 20, "hello"]
str := Join(",", arr*)

AssertEq(str, "10,20,hello", A_LineNumber)

FileAppend "pass", "*"
