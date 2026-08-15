#NoTrayIcon

#import KS { Join }
str := Join(",", "1", "2", "3")

if (str == "1,2,3")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

str := Join(",", 1, 2, 3)

if (str == "1,2,3")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

arr := [10, 20, "hello"]
str := Join(",", arr*)

if (str == "10,20,hello")
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
