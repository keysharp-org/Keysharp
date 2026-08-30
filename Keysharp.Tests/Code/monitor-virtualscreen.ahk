#NoTrayIcon
#Include <assert>
#import KS { Monitor }

v := Monitor.VirtualScreen

; The rect shape is X/Y/Width/Height, the same one Bounds and WorkArea use.
Assert(v.Width > 0 && v.Height > 0, A_LineNumber)

; The union must contain the primary monitor.
p := Monitor.Primary
Assert(v.X <= p.X && v.Y <= p.Y, A_LineNumber)
Assert(v.X + v.Width >= p.X + p.Width, A_LineNumber)
Assert(v.Y + v.Height >= p.Y + p.Height, A_LineNumber)

; ...and equal the union folded over every monitor, origin included: X is negative when a
; display sits left of the primary.
left := "", top := "", right := "", bottom := ""

for m in Monitor.All
{
	left := (left = "") ? m.X : Min(left, m.X)
	top := (top = "") ? m.Y : Min(top, m.Y)
	right := (right = "") ? m.X + m.Width : Max(right, m.X + m.Width)
	bottom := (bottom = "") ? m.Y + m.Height : Max(bottom, m.Y + m.Height)
}

Assert(v.X = left && v.Y = top, A_LineNumber)
Assert(v.Width = right - left && v.Height = bottom - top, A_LineNumber)

FileAppend "pass", "*"
