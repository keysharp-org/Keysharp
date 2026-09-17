#ErrorStdOut
#Warn All, StdOut
#Warn Experimental, Off
#NoTrayIcon
#import KS { Monitor }
#Include <assert>

count := MonitorGetCount()
primary := MonitorGetPrimary()
AssertEq(Monitor.Count, count, A_LineNumber)

; Index 0 and an omitted index both mean the primary; any other index outside the count is a ValueError, as in AHK.
AssertEq(MonitorGet(0), primary, A_LineNumber)
AssertEq(MonitorGet(), primary, A_LineNumber)
Throws(() => MonitorGet(count + 1), A_LineNumber, ValueError)
Throws(() => MonitorGet(-1), A_LineNumber, ValueError)
Throws(() => MonitorGetWorkArea(count + 1), A_LineNumber, ValueError)
Throws(() => MonitorGetName(count + 1), A_LineNumber, ValueError)
Throws(() => Monitor(count + 1), A_LineNumber, ValueError)

monitors := Monitor.All
Assert(monitors is Array, A_LineNumber)
AssertEq(monitors.Length, count, A_LineNumber)

for i, m in monitors
{
	AssertEq(m.Index, i, A_LineNumber)
	AssertEq(m.Name, MonitorGetName(i), A_LineNumber)
	MonitorGet(i, &l, &t, &r, &b)
	AssertEq(m.X, l, A_LineNumber)
	AssertEq(m.Y, t, A_LineNumber)
	AssertEq(m.Width, r - l, A_LineNumber)
	AssertEq(m.Height, b - t, A_LineNumber)

	; Metadata is hardware-dependent: each field is a plausible value or "", never a fabricated stand-in.
	hz := m.RefreshRate
	Assert(hz == "" || (hz is Float && hz > 1 && hz < 1000), A_LineNumber)
	deg := m.Orientation
	Assert(deg == 0 || deg == 90 || deg == 180 || deg == 270, A_LineNumber)
	mm := m.PhysicalWidth
	Assert(mm == "" || (mm is Integer && mm > 0 && mm < 5000), A_LineNumber)
	conn := m.Connection
	Assert(conn == "" || conn == "HDMI" || conn == "DisplayPort" || conn == "eDP" || conn == "DVI" || conn == "VGA" || conn == "Internal", A_LineNumber)

	; The probe answers rather than throws; an unsupported monitor reports an OSError, not a made-up level.
	if m.IsBrightnessSupported
	{
		level := m.Brightness
		Assert(level >= 0 && level <= 100, A_LineNumber)
	}
	else
		Throws(() => m.Brightness, A_LineNumber, OSError)

	; Whatever Id a monitor reports must find that same monitor again.
	if ((id := m.Id) != "")
	{
		found := Monitor.FromId(id)
		Assert(found is Monitor, A_LineNumber)

		if (found is Monitor)
		{
			AssertEq(found.Index, m.Index, A_LineNumber)
			AssertEq(found.Name, m.Name, A_LineNumber)
		}
	}
}

AssertEq(Monitor.FromId("no-such-monitor-id"), "", A_LineNumber)
AssertEq(Monitor.FromId(""), "", A_LineNumber)

p := Monitor.Primary
AssertEq(p.Index, primary, A_LineNumber)
Assert(p.IsPrimary && p.Scale > 0, A_LineNumber)
AssertEq(Monitor.FromPoint(p.X + p.Width // 2, p.Y + p.Height // 2).Index, primary, A_LineNumber)

; Refresh re-reads in place and returns the same object so it can be chained.
AssertEq(p.Refresh(), p, A_LineNumber)
AssertEq(p.Index, primary, A_LineNumber)

; Out-of-range VCP codes and values are rejected before anything reaches the monitor.
Throws(() => p.GetVCP(-1), A_LineNumber, ValueError)
Throws(() => p.GetVCP(256), A_LineNumber, ValueError)
Throws(() => p.SetVCP(-1, 0), A_LineNumber, ValueError)
Throws(() => p.SetVCP(256, 0), A_LineNumber, ValueError)
Throws(() => p.SetVCP(0, -1), A_LineNumber, ValueError)
Throws(() => p.SetVCP(0, 65536), A_LineNumber, ValueError)

; OnChange returns a running hook that Stop() ends. No display change is made, so the callback never runs.
changes := []
hook := Monitor.OnChange((h, kind) => changes.Push(kind))
Assert(hook.InProgress && hook.EndReason == "", A_LineNumber)
hook.Stop()
Assert(!hook.InProgress, A_LineNumber)
AssertEq(hook.EndReason, "Stopped", A_LineNumber)
AssertEq(changes.Length, 0, A_LineNumber)

Throws(() => Monitor.OnChange("not a function"), A_LineNumber, TypeError)

FileAppend "pass", "*"
