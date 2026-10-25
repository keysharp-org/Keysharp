#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut
#Requires AutoHotkey v2.1-alpha.37
#Import Consumer
#Import Recorder
#Include <assert>

#Startup Value := (40 + 2)
#Startup Fraction := .5
#Startup Counter := 0, Counter++
#Startup BeforeAuto := IsSet(AutoExecuted)
#Startup Initialize
#Startup Initialize()
#Startup (NoValue())
#Startup ((NoValue()), NoValue())
#Startup (
    Recorder.Record("multiline")
)

AutoExecuted := true
AssertEq(Value, 42, A_LineNumber)
AssertEq(Fraction, 0.5, A_LineNumber)
AssertEq(Counter++, 1, A_LineNumber)
AssertEq(BeforeAuto, false, A_LineNumber)
AssertEq(Recorder.Events.Join(","), "dependency,consumer,main,multiline,last", A_LineNumber)
FileAppend "pass", "*"

#Startup Recorder.Record("last")

Initialize() {
    Recorder.Record("main")
    return "ignored"
}

NoValue() {
    return
}

#Module Consumer
#Import Dependency
#Import Recorder
#Startup Recorder.Record("consumer")

#Module Dependency
#Import Recorder
#Startup Recorder.Record("dependency")

#Module Recorder
#Startup Events := []
Record(Value) => Events.Push(Value)
