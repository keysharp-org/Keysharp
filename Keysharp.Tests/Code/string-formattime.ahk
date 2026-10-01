#NoTrayIcon
#Include <assert>

; Names, separators, the calendar and the week rule all come from the locale, so every call that depends on one
; pins en-US with L1033. Only purely numeric day/month/hour/minute/second specifiers run on the current locale.
x := "20200704070809"

AssertEq(FormatTime(x, "d"), "4", A_LineNumber)

AssertEq(FormatTime(x, "dd"), "04", A_LineNumber)

AssertEq(FormatTime(x " L1033", "ddd"), "Sat", A_LineNumber)

AssertEq(FormatTime(x " L1033", "dddd"), "Saturday", A_LineNumber)

AssertEq(FormatTime(x, "M"), "7", A_LineNumber)

AssertEq(FormatTime(x, "MM"), "07", A_LineNumber)

AssertEq(FormatTime(x " L1033", "MMM"), "Jul", A_LineNumber)

AssertEq(FormatTime(x " L1033", "MMMM"), "July", A_LineNumber)

AssertEq(FormatTime(x " L1033", "y"), "20", A_LineNumber)

AssertEq(FormatTime(x " L1033", "yy"), "20", A_LineNumber)

AssertEq(FormatTime("20020704 L1033", "y"), "2", A_LineNumber)

AssertEq(FormatTime("20020704 L1033", "yy"), "02", A_LineNumber)

AssertEq(FormatTime(x " L1033", "yyyy"), "2020", A_LineNumber)

AssertEq(FormatTime(x " L1033", "gg"), "AD", A_LineNumber)

AssertEq(FormatTime(x, "h"), "7", A_LineNumber)

AssertEq(FormatTime(x, "hh"), "07", A_LineNumber)

AssertEq(FormatTime(x, "H"), "7", A_LineNumber)

AssertEq(FormatTime("20200704200809", "HH"), "20", A_LineNumber)

AssertEq(FormatTime(x, "m"), "8", A_LineNumber)

AssertEq(FormatTime(x, "mm"), "08", A_LineNumber)

AssertEq(FormatTime(x, "s"), "9", A_LineNumber)

AssertEq(FormatTime(x, "ss"), "09", A_LineNumber)

AssertEq(FormatTime(x " L1033", "t"), "A", A_LineNumber)

AssertEq(FormatTime(x " L1033", "tt"), "AM", A_LineNumber)

AssertEq(FormatTime(x " L1033", "Time"), "7:08 AM", A_LineNumber)

AssertEq(FormatTime(x " L1033", "shortdate"), "7/4/2020", A_LineNumber)

AssertEq(FormatTime(x " L1033", "LongDate"), "Saturday, July 4, 2020", A_LineNumber)

AssertEq(FormatTime(x " L1033", "YDay"), "186", A_LineNumber)

AssertEq(FormatTime("20200101 L1033", "YDay0"), "001", A_LineNumber)

AssertEq(FormatTime(x " L1033", "WDay"), "7", A_LineNumber)

AssertEq(FormatTime(x " L1033", "YWeek"), "202027", A_LineNumber)

; YWeek is the ISO 8601 week-numbering year, which differs from the calendar year around New Year.
AssertEq(FormatTime("20050101", "YWeek"), "200453", A_LineNumber)

AssertEq(FormatTime("20210103", "YWeek"), "202053", A_LineNumber)

AssertEq(FormatTime("20241230", "YWeek"), "202501", A_LineNumber)

AssertEq(FormatTime("20260105", "YWeek"), "202602", A_LineNumber)

; A timestamp may be truncated after any pair of digits; the omitted parts default to the start of the period.
AssertEq(FormatTime("2020 L1033", "yyyyMMddHHmmss"), "20200101000000", A_LineNumber)

AssertEq(FormatTime("202007 L1033", "yyyyMMddHHmmss"), "20200701000000", A_LineNumber)

AssertEq(FormatTime("20200704 L1033", "yyyyMMddHHmmss"), "20200704000000", A_LineNumber)

AssertEq(FormatTime("2020070420 L1033", "yyyyMMddHHmmss"), "20200704200000", A_LineNumber)

AssertEq(FormatTime("202007042030 L1033", "yyyyMMddHHmmss"), "20200704203000", A_LineNumber)

AssertEq(FormatTime("20200704203040 L1033", "yyyyMMddHHmmss"), "20200704203040", A_LineNumber)

; A blank or omitted format gives the time followed by the long date, or the reverse with R.
AssertEq(FormatTime(x " L1033", ""), "7:08 AM Saturday, July 4, 2020", A_LineNumber)

AssertEq(FormatTime(x " L1033"), "7:08 AM Saturday, July 4, 2020", A_LineNumber)

; The locale's own short time pattern may separate AM with a no-break space.
y := StrReplace(StrReplace(FormatTime(x " L1033 R"), Chr(0xA0), " "), Chr(0x202F), " ")

AssertEq(y, "Saturday, July 4, 2020 7:08 AM", A_LineNumber)

; LSys is the current locale without user overrides, which never alter purely numeric specifiers.
AssertEq(FormatTime(x " LSys", "yyyyMMddHHmmss"), FormatTime(x, "yyyyMMddHHmmss"), A_LineNumber)

; es-MX, by decimal and by hexadecimal LCID. Its AM marker varies in spacing and dots between ICU versions.
for lcid in ["L2058", "L0x80A"]
{
	y := StrReplace(StrReplace(FormatTime(x " " lcid, ""), Chr(0xA0), " "), Chr(0x202F), " ")
	Assert(RegExMatch(y, "i)^7:08 a *\. *m"), A_LineNumber)
	Assert(InStr(y, "julio 4, 2020"), A_LineNumber)
}

AssertEq(FormatTime(x " L1033", "yyyyM"), "20207", A_LineNumber)

AssertEq(FormatTime(x " L1033", "yyyyMM"), "202007", A_LineNumber)

AssertEq(FormatTime(x " L1033", "yyyyMMM"), "2020Jul", A_LineNumber)

AssertEq(FormatTime(x " L1033", "yyyyMMMM"), "2020July", A_LineNumber)

AssertEq(FormatTime(x " L1033", "yyyyMMMMd"), "2020July4", A_LineNumber)

AssertEq(FormatTime(x " L1033", "yyyyMMMMdd"), "2020July04", A_LineNumber)

AssertEq(FormatTime(x " L1033", "yyyyMMMMddd"), "2020JulySat", A_LineNumber)

AssertEq(FormatTime(x " L1033", "yyyyMMMMdddd"), "2020JulySaturday", A_LineNumber)

x := "20200704200809"

AssertEq(FormatTime(x " L1033", "yyyyMMMMdddd hh"), "2020JulySaturday 08", A_LineNumber)

AssertEq(FormatTime(x " L1033", "yyyyMMMMdddd HH"), "2020JulySaturday 20", A_LineNumber)

AssertEq(FormatTime(x " L1033", "'Date:' yyyyMMMMdddd"), "Date: 2020JulySaturday", A_LineNumber)

AssertEq(FormatTime(x " L1033", "'Date:' yyyyMMMMdddd ''''"), "Date: 2020JulySaturday '", A_LineNumber)

; As in AutoHotkey, '' outside quoted text is an empty quoted section, and every character which is not a date or time
; specifier is itself, including those .NET would read as its own.
AssertEq(FormatTime(x " L1033", "'Date:' yyyyMMMMdddd `"''`""), "Date: 2020JulySaturday `"`"", A_LineNumber)
AssertEq(FormatTime(x " L1033", "Week of yyyy, hh:mm:ss tt zz f K %\"), "Week of 2020, 08:08:09 PM zz f K %\", A_LineNumber)
AssertEq(FormatTime(x " L1033", "H 'o''clock'"), "20 o'clock", A_LineNumber)
AssertEq(FormatTime(x " L1033", "d") "|" FormatTime(x " L1033", "y"), "4|20", A_LineNumber)
AssertEq(FormatTime(x " L1033", "''") "|" FormatTime(x " L1033", "'") "|" FormatTime(x " L1033", " ''"), "|| ", A_LineNumber)
AssertEq(FormatTime(x " L1033", "yyy|yyyyy|yyyyyy'x'"), "2020|2020|2020x", A_LineNumber)
AssertEq(FormatTime(x " L1033", " `tTime") "|" FormatTime(x " L1033", " YDay") "|" FormatTime(x " L1033", "Time "), "8:08 PM|186|Ti8e ", A_LineNumber)

; An empty quoted section separates the specifiers around it.
AssertEq(FormatTime(x " L1033", "d''d|yyy''yy|dd''|''dd"), "44|202020|04|04", A_LineNumber)

; Options without a timestamp, or after a tab, apply to the current time.
AssertEq(FormatTime(" L1033", "yyyy"), FormatTime(, "yyyy"), A_LineNumber)
AssertEq(FormatTime("20200704`tL1031", "MMMM"), "Juli", A_LineNumber)
AssertEq(FormatTime("20200704 D Tx", "yyyy"), "2020", A_LineNumber)
AssertEq(FormatTime("20200704 L99999", "yyyy") "|" FormatTime("L-1", "yyyy"), "|", A_LineNumber)
AssertEq(FormatTime(x " L99999", "YDay") "|" FormatTime(x " L99999", "WDay") "|" FormatTime(x " L99999", "Time"), "186|7|", A_LineNumber)
AssertEq(FormatTime("20200704 L1024", "MMMM") "|" FormatTime("20200704 L3072", "MMMM"), FormatTime("20200704", "MMMM") "|" FormatTime("20200704", "MMMM"), A_LineNumber)

FileAppend "pass", "*"
