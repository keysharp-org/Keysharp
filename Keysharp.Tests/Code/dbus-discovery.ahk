#ErrorStdOut
#Warn All, StdOut
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

; The test peer supplies a container with child nodes but no declared interfaces.
container := ComObject("io.keysharp.CoreTest:/io/keysharp")
containerXml := container.Introspect()
Assert(!InStr(containerXml, "<interface"), A_LineNumber)
Assert(InStr(containerXml, '<node name="CoreTest"'), A_LineNumber)
peer := container["CoreTest"]
AssertEq(peer.Echo("child"), "echo:child", A_LineNumber)
Assert(InStr(peer.Introspect(), '<interface name="io.keysharp.CoreTest"'), A_LineNumber)
pinned := ComObjQuery(peer, "io.keysharp.CoreTest")
Throws(() => pinned.Introspect(), A_LineNumber, MethodError)
Throws(() => container.MissingMethod(), A_LineNumber, MethodError)

; A name two of the object's own interfaces define needs the interface pinned, while one of its own interfaces
; shadows a standard interface defining the same name.
Throws(() => peer.Twin(), A_LineNumber, MethodError)
Throws(() => peer.Shared, A_LineNumber, PropertyError)
second := ComObjQuery(peer, "io.keysharp.CoreTest.Second")
AssertEq(second.Twin(), "io.keysharp.CoreTest.Second.Twin", A_LineNumber)
AssertEq(second.Shared, "io.keysharp.CoreTest.Second.Shared", A_LineNumber)
AssertEq(peer.Ping(), "io.keysharp.CoreTest.Second.Ping", A_LineNumber)

FileAppend("pass`n", "*")
