#Include <assert>
include_again_counter := 0

#include directive-include-target.ahk
#includeagain "directive-include-target.ahk"

class IncludedClass
{
	#include "directive-include-target-class.ahk"
}

obj := {
	#include
(
	directive-include-target-object.ahk
)
}

inst := IncludedClass()

AssertEq(include_value, 123, A_LineNumber)

AssertEq(include_again_counter, 2, A_LineNumber)

AssertEq(inst.IncludedMethod(), 42, A_LineNumber)

AssertEq(obj.Alpha, 10, A_LineNumber)

AssertEq(obj.Beta, 20, A_LineNumber)

FileAppend "pass", "*"

ExitApp()
