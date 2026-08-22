#NoTrayIcon
#Include <assert>

ticks := []
fn := (*) => (ticks.Push(A_TickCount), Sleep(20))
ok := true

SetTimer(fn, 80)
Sleep(760)
SetTimer(fn, 0)
Sleep(50)

if (ticks.Length < 6)
    ok := false

if (ok && ticks.Length > 1)
{
    Loop ticks.Length - 1
    {
        delta := ticks[A_Index + 1] - ticks[A_Index]

        if (delta < 50 || delta > 160)
        {
            ok := false
            break
        }
    }
}

Assert(ok, A_LineNumber)

FileAppend "pass", "*"
