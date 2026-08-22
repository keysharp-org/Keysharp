#NoTrayIcon
#Include <assert>

x := "20200704070809"
y := FormatTime(x, "d")

Assert(y = "4", A_LineNumber)

y := FormatTime(x, "dd")

Assert(y = "04", A_LineNumber)
	
y := FormatTime(x, "ddd")

Assert(y = "Sat", A_LineNumber)
	
y := FormatTime(x, "dddd")

Assert(y = "Saturday", A_LineNumber)
	
y := FormatTime(x, "M")

Assert(y = "7", A_LineNumber)
	
y := FormatTime(x, "MM")

Assert(y = "07", A_LineNumber)
	
y := FormatTime(x, "yyyy")

Assert(y = "2020", A_LineNumber)
	
y := FormatTime(x, "shortdate")

Assert(y = "7/4/2020", A_LineNumber)
	
y := FormatTime(x, "LongDate")

Assert(y = "Saturday, July 4, 2020", A_LineNumber)
	
y := FormatTime(x, "'Date:' yyyyMMMMdddd")

Assert(y = "Date: 2020JulySaturday", A_LineNumber)
	
y := FormatTime(x, "'Date:' yyyyMMMMdddd ''''")

Assert(y = "Date: 2020JulySaturday '", A_LineNumber)
	
y := FormatTime(x, "'Date:' yyyyMMMMdddd `"''`"")

Assert(y = "Date: 2020JulySaturday '", A_LineNumber)

FileAppend "pass", "*"
