#NoTrayIcon
#Include <assert>

x := A_YYYY
y := A_Year

Assert(x > 2000, A_LineNumber)

Assert(y = x, A_LineNumber)

x := A_MM
y := A_Mon

Assert(x >= 1 && x <= 12, A_LineNumber)

Assert(y = x, A_LineNumber)

x := A_DD
y := A_MDay

Assert(x >= 1 && x <= 31, A_LineNumber)

Assert(y = x, A_LineNumber)

x := A_MMMM

Assert(x = "January" || x = "February" || x = "March" || x = "April" || x = "May" || x = "June" || x = "July" || x = "August" || x = "September" || x = "October" || x = "November" || x = "December", A_LineNumber)

x := A_MMM

Assert(x = "Jan" || x = "Feb" || x = "Mar" || x = "Apr" || x = "May" || x = "Jun" || x = "Jul" || x = "Aug" || x = "Sep" || x = "Oct" || x = "Nov" || x = "Dec", A_LineNumber)

x := A_DDDD

Assert(x = "Sunday" || x = "Monday" || x = "Tuesday" || x = "Wednesday" || x = "Thursday" || x = "Friday" || x = "Sunday" || x = "Saturday", A_LineNumber)

x := A_DDD

Assert(x = "Sun" || x = "Mon" || x = "Tue" || x = "Wed" || x = "Thu" || x = "Fri" || x = "Sun" || x = "Sat", A_LineNumber)

x := A_WDay

Assert(x >= 1 && x <= 7, A_LineNumber)

x := A_YDay

Assert(x >= 1 && x <= 366, A_LineNumber)

x := A_YWeek

Assert(x != "", A_LineNumber)

x := A_Hour

Assert(x >= 0 && x <= 23, A_LineNumber)

x := A_Min

Assert(x >= 0 && x <= 59, A_LineNumber)

x := A_Sec

Assert(x >= 0 && x <= 59, A_LineNumber)

x := A_MSec

Assert(x >= 0 && x <= 999, A_LineNumber)

x := A_Now

Assert(x != "", A_LineNumber)

x := A_NowUTC

Assert(x != "", A_LineNumber)

x := A_TickCount

Assert(x > 0, A_LineNumber)

; Pin every date variable to one clock reading. A pass whose reads straddle a second boundary is retried.
stable := false

Loop 5
{
	now := A_Now
	nowUtc := A_NowUTC
	yyyy := A_YYYY
	mm := A_MM
	dd := A_DD
	mmmm := A_MMMM
	mmm := A_MMM
	dddd := A_DDDD
	ddd := A_DDD
	wday := A_WDay
	yday := A_YDay
	yweek := A_YWeek
	hh := A_Hour
	mi := A_Min
	ss := A_Sec

	if (A_Now == now)
	{
		stable := true
		break
	}
}

Assert(stable, A_LineNumber)

Assert(IsTime(now) && StrLen(now) == 14, A_LineNumber)

Assert(IsTime(nowUtc) && StrLen(nowUtc) == 14, A_LineNumber)

; Both were read within the same second, so they differ by exactly the UTC offset.
utcOffset := DateDiff(now, nowUtc, "Seconds")

Assert(Mod(utcOffset, 900) == 0 && Abs(utcOffset) <= 14 * 3600, A_LineNumber)

AssertEq(String(yyyy), FormatTime(now, "yyyy"), A_LineNumber)

AssertEq(mm, FormatTime(now, "MM"), A_LineNumber)

AssertEq(dd, FormatTime(now, "dd"), A_LineNumber)

AssertEq(mmmm, FormatTime(now, "MMMM"), A_LineNumber)

AssertEq(mmm, FormatTime(now, "MMM"), A_LineNumber)

AssertEq(dddd, FormatTime(now, "dddd"), A_LineNumber)

AssertEq(ddd, FormatTime(now, "ddd"), A_LineNumber)

AssertEq(String(wday), FormatTime(now, "WDay"), A_LineNumber)

AssertEq(String(yday), FormatTime(now, "YDay"), A_LineNumber)

AssertEq(hh, FormatTime(now, "HH"), A_LineNumber)

AssertEq(mi, FormatTime(now, "mm"), A_LineNumber)

AssertEq(ss, FormatTime(now, "ss"), A_LineNumber)

; A_YWeek is ISO 8601: the week and year of the Thursday in this Monday-based week.
YearDays(y) => (Mod(y, 4) == 0 && Mod(y, 100) != 0) || Mod(y, 400) == 0 ? 366 : 365
isoYear := yyyy + 0
thursday := yday - Mod(wday + 5, 7) + 3

if (thursday < 1)
	thursday += YearDays(--isoYear)
else if (thursday > YearDays(isoYear))
	thursday -= YearDays(isoYear++)

AssertEq(yweek, isoYear . SubStr("0" . ((thursday - 1) // 7 + 1), -2), A_LineNumber)

FileAppend "pass", "*"
