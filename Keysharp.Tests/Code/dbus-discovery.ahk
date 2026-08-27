#ErrorStdOut
#Include <assert>

bus := ComObject("org.freedesktop.DBus")
xml := bus.Introspect()
Assert(InStr(xml, "<interface name="), A_LineNumber)

ifaces := []
pos := 1
while pos := RegExMatch(xml, '<interface name="([^"]+)"', &m, pos)
{
    ifaces.Push(m[1])
    pos += m.Len
}
Assert(ifaces.Length > 1, A_LineNumber)

props := ComObjQuery(bus, "org.freedesktop.DBus.Properties")
all := props.GetAll("org.freedesktop.DBus")
Assert(all is Map, A_LineNumber)
Assert(all.Has("Interfaces"), A_LineNumber)
Assert(bus.Interfaces is Array, A_LineNumber)
AssertEq(ComObjType(bus, "Name"), "org.freedesktop.DBus", A_LineNumber)
AssertEq(ComObjType(bus, "Path"), "/org/freedesktop/DBus", A_LineNumber)

names := bus.ListNames()
Assert(names is Array && names.Length > 0, A_LineNumber)

; --- child objects, as the ComObject documentation example walks them ---
root := ComObject("org.freedesktop.DBus:/")
rootXml := root.Introspect()
kids := 0
pos := 1
while pos := RegExMatch(rootXml, '<node name="([^"]+)"', &m, pos)
{
    child := root[m[1]]
    Assert(InStr(ComObjType(child, "Path"), "/") == 1, A_LineNumber)
    kids += 1
    pos += m.Len
}
Assert(kids > 0, A_LineNumber)

FileAppend("pass`n", "*")
