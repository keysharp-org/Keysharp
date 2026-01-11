

x := 1
m := { x : 1, two : 2, three : 3 }
a := [10, 20, 30]

if (IsInteger(x))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := -1

if (IsInteger(x))
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 1.234

if (IsInteger(x) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := "1234"

if (IsInteger(x) == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := "-1234"

if (IsInteger(x) == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := "+1234"

if (IsInteger(x) == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := "1234.1234"

if (IsInteger(x) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := "-1234.1234"

if (IsInteger(x) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := "+1234.1234"

if (IsInteger(x) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsInteger(a) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := 1.234

if (IsFloat(x) == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := -1.234

if (IsFloat(x) == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := "1234"

if (IsFloat(x) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := "-1234"

if (IsFloat(x) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

x := "+1234"

if (IsFloat(x) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsFloat(a) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsNumber(0) == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsNumber(1) == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsNumber(-1) == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsNumber(1.234) == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsNumber(-1.234) == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsNumber("1234") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsNumber("-1234") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsNumber("+1234") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsNumber("1.234") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsNumber("-1.234") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsNumber("+1.234") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsNumber(a) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsNumber("A") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsNumber("ABCDEF") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsNumber("0xA") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsNumber("0xABCDEF") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsObject(0) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsObject(1.234) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsObject("test") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsObject(a) == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsObject(m) == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsObject(ComObjArray(13, 1)) == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsDigit(1) == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsDigit(-1) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsDigit(1.234) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsDigit("0123456789") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsDigit("1A") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsDigit("A1") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsDigit("0x01") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsDigit(a) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsDigit(m) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsXDigit(1) == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsXDigit(-1) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsXDigit(1.234) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsXDigit("0123456789") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsXDigit("1A") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsXDigit("0x01ABCdef") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsXDigit("0xg") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsXDigit(a) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsXDigit(m) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsAlpha(1) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsAlpha(-1) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsAlpha(1.234) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsAlpha("0123456789") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsAlpha("ABC") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsAlpha("abc") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsAlpha("ABC123") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsAlpha(".") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsAlpha(a) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsAlpha(m) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsUpper(1) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsUpper(-1) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsUpper(1.234) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsUpper("0123456789") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsUpper("ABC") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsUpper("abc") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsUpper("AbC123") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsUpper(".") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsUpper(a) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsUpper(m) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsLower(1) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsLower(-1) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsLower(1.234) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsLower("0123456789") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsLower("ABC") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsLower("abc") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsLower("AbC123") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsLower(".") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsLower(a) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsLower(m) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsAlnum(1) == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsAlnum(-1) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsAlnum(1.234) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsAlnum("0123456789") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsAlnum("ABC") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsAlnum("abc") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsAlnum("AbC123") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsAlnum(".") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsAlnum(a) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsAlnum(m) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsSpace(1) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsSpace(-1) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsSpace(1.234) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsSpace("0123456789") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsSpace("ABC") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsSpace("abc") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsSpace("AbC123") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsSpace(".") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsSpace(" `t`n`r`v`f") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsSpace(a) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsSpace(m) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsTime("2021") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsTime("202106") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsTime("202199") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsTime("20211201") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsTime("20211299") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsTime("2021121513") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsTime("2021121555") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsTime("202112152033") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsTime("202112152099") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsTime("20211215203522") == 1)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsTime("20211215203599") == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"
	
if (IsTime(a) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"

if (IsTime(m) == 0)
	FileAppend "pass", "*"
else
	FileAppend "fail", "*"