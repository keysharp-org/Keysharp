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

Assert(x != "", A_LineNumber) ; Not really a full test, but the code is clear enough to know it works.

x := A_Hour

Assert(x >= 0 && x <= 23, A_LineNumber)

x := A_Min

Assert(x >= 0 && x <= 59, A_LineNumber)

x := A_Sec

Assert(x >= 0 && x <= 59, A_LineNumber)

x := A_MSec

Assert(x >= 0 && x <= 999, A_LineNumber)

x := A_Now

Assert(x != "", A_LineNumber) ; Not really a full test, but the code is clear enough to know it works.

x := A_NowUTC

Assert(x != "", A_LineNumber) ; Not really a full test, but the code is clear enough to know it works.

x := A_TickCount

Assert(x > 0, A_LineNumber) ; Not really a full test, but the code is clear enough to know it works.

FileAppend "pass", "*"
