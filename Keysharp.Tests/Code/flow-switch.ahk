#NoTrayIcon
#Include <assert>
#Import Ks { StringBuffer }

x := 1
z := ""

switch x
{
	case 3:
		z := 3
	case 2:
		z := 2
	case 1:
		z := 1
}

AssertEq(z, 1, A_LineNumber)

z := ""

switch x {
	case 3:
		z := 3
	case 2:
		z := 2
	default:
		z := 1
}

AssertEq(z, 1, A_LineNumber)

z := ""

switch x
{
	case 3:
	case 2:
	case 1:
		z := 1
}

AssertEq(z, 1, A_LineNumber)
	
z := ""

switch x	{
	default:
		z := 1
}

AssertEq(z, 1, A_LineNumber)

z := ""

switch x
{
	case 3, 2, 1:
		z := 1
}

AssertEq(z, 1, A_LineNumber)

x := "Tester"
z := ""

switch x, 0
{
	case "mismatch":
		z := 3
	case "notthis":
		z := 2
	case "tester":
		z := 1
}

AssertEq(z, 1, A_LineNumber)

x := "Tester"
z := ""

switch x, 1 {
	case "mismatch":
		z := 3
	case "notthis":
		z := 2
	case "tester":
		z := 0
	case "Tester":
		z := 1
}

AssertEq(z, 1, A_LineNumber)

x := "Tester"
z := ""

switch x, 1
{
	case "mismatch", "notthis", "tester":
		z := 2
	case "Tester":
		z := 1
}

AssertEq(z, 1, A_LineNumber)

x := 1
z := ""

switch
{
	case x == 3:
		z := 3
	case x == 2:
		z := 2
	case x == 1:
		z := 1
}

AssertEq(z, 1, A_LineNumber)

x := 1
z := ""

switch
{
	case x > 5:
		z := 3
	case x > 0 && x < 4:
		z := 1
	default:
		z := 2
}

AssertEq(z, 1, A_LineNumber)

x := 1
z := ""
y := ""

switch {
	case "":
		z := 3
	case y:
		z := 2
	case 123:
		z := 1
}

AssertEq(z, 1, A_LineNumber)

x := 123
z := ""

switch x, 1 ; this is a comment
{
	case "mismatch": ; another comment
		mism:
		z := 3
	case "notthis":
		z := 2
	case 123:
		goto mism ; last comment
	case "Tester":
		z := 1
}

AssertEq(z, 3, A_LineNumber)

x := 0
z := 0

switch z
{
	case 10:
		x += 100
	case 20:
		x += 100
	case 30:
		x += 100
}

AssertEq(x, 0, A_LineNumber)
	
x := 3
y := 4
z := 0

func(m, n)
{
	return m * n
}

switch func(x, y)
{   
    case 1:  z := 1
    case 2:  z := 2
    case 12: z := 3
	default: z := 4
}

AssertEq(z, 3, A_LineNumber)

x := 3
y := 4
z := 0

switch func(x, y) {   
    case 1, 2, func(3, 4): z := 1
	default: z := 2
}

AssertEq(z, 1, A_LineNumber)

class myclass
{
	func(m, n)
	{
		return m * n
	}
}

myclassobj := myclass()

x := 3
y := 4
z := 0

switch myclassobj.func(x, y) {   
    case 1, 2, myclassobj.func(3, 4): z := 1
	default: z := 2
}

AssertEq(z, 1, A_LineNumber)

MyFunc()

myfunc() {
	x := 1
	z := ""

	switch x
	{
		case 3:
			z := 3
		case 2:
			z := 2
		case 1:
			z := 1
	}

	AssertEq(z, 1, A_LineNumber)

	z := ""

	switch x {
		case 3:
			z := 3
		case 2:
			z := 2
		default:
			z := 1
	}

	AssertEq(z, 1, A_LineNumber)

	z := ""

	switch x
	{
		case 3:
		case 2:
		case 1:
			z := 1
	}

	AssertEq(z, 1, A_LineNumber)
	
	z := ""

	switch x	{
		default:
			z := 1
	}

	AssertEq(z, 1, A_LineNumber)

	z := ""

	switch x
	{
		case 3, 2, 1:
			z := 1
	}

	AssertEq(z, 1, A_LineNumber)

	x := "Tester"
	z := ""

	switch x, 0
	{
		case "mismatch":
			z := 3
		case "notthis":
			z := 2
		case "tester":
			z := 1
	}

	AssertEq(z, 1, A_LineNumber)

	x := "Tester"
	z := ""

	switch x, 1 {
		case "mismatch":
			z := 3
		case "notthis":
			z := 2
		case "tester":
			z := 0
		case "Tester":
			z := 1
	}

	AssertEq(z, 1, A_LineNumber)

	x := "Tester"
	z := ""

	switch x, 1
	{
		case "mismatch", "notthis", "tester":
			z := 2
		case "Tester":
			z := 1
	}

	AssertEq(z, 1, A_LineNumber)

	x := 1
	z := ""

	switch
	{
		case x == 3:
			z := 3
		case x == 2:
			z := 2
		case x == 1:
			z := 1
	}

	AssertEq(z, 1, A_LineNumber)

	x := 1
	z := ""

	switch
	{
		case x > 5:
			z := 3
		case x > 0 && x < 4:
			z := 1
		default:
			z := 2
	}

	AssertEq(z, 1, A_LineNumber)

	x := 1
	z := ""
	y := ""

	switch {
		case "":
			z := 3
		case y:
			z := 2
		case 123:
			z := 1
	}

	AssertEq(z, 1, A_LineNumber)

	x := 123
	z := ""

	switch x, 1 ; this is a comment
	{
		case "mismatch": ; another comment
			mism:
			z := 3
		case "notthis":
			z := 2
		case 123:
			goto mism ; last comment
		case "Tester":
			z := 1
	}

	AssertEq(z, 3, A_LineNumber)

	x := 0
	z := 0

	switch z
	{
		case 10:
			x += 100
		case 20:
			x += 100
		case 30:
			x += 100
	}

	AssertEq(x, 0, A_LineNumber)
}

; Without CaseSense, a number and a numeric string compare as numbers, two strings as strings, objects by identity.
SwitchNumber(v) {
	switch v {
		case 9: return "nine"
		case 2: return "two"
		case 1: return "one"
		default: return "default"
	}
}
AssertEq(SwitchNumber("09"), "nine", A_LineNumber)
AssertEq(SwitchNumber(10 / 5), "two", A_LineNumber)
AssertEq(SwitchNumber("01"), "one", A_LineNumber)
switchResult := ""
switch "01" {
	case "1": switchResult := "numeric"
	default: switchResult := "string"
}
AssertEq(switchResult, "string", A_LineNumber)
switchMap := Map()
switch switchMap {
	case Map(): switchResult := "other"
	case switchMap: switchResult := "same"
	default: switchResult := "none"
}
AssertEq(switchResult, "same", A_LineNumber)

; Any CaseSense, whether false or held in a variable, compares every case as a string.
switchCaseSense := "Off"
switch "ABC", switchCaseSense {
	case "abc": switchResult := "matched"
	default: switchResult := "missed"
}
AssertEq(switchResult, "matched", A_LineNumber)
switch "ABC", false {
	case "abc": switchResult := "matched"
	default: switchResult := "missed"
}
AssertEq(switchResult, "matched", A_LineNumber)
switch 1, true {
	case "1.0": switchResult := "numeric"
	default: switchResult := "string"
}
AssertEq(switchResult, "string", A_LineNumber)
SwitchObjectCaseSense() {
	switch [], "On" {
		default: return
	}
}
Throws(SwitchObjectCaseSense, A_LineNumber, TypeError)

; A CaseSense is an ordinary expression: a dynamic reference or an assignment in it resolves like any other.
SwitchDynamicCaseSense() {
	name := "cs", cs := "Off"
	switch "A", %name% {
		case "a": return "matched"
	}
	return "missed"
}
AssertEq(SwitchDynamicCaseSense(), "matched", A_LineNumber)
SwitchAssignedCaseSense() {
	switch "A", mode := "Off" {
		case "a": return mode
	}
	return "missed"
}
AssertEq(SwitchAssignedCaseSense(), "Off", A_LineNumber)


; A case matches as == would, whether the case is computed or an integer literal.
switchValues := [1, "1", 1.0, "1.0", "abc", "ABC"]
for switchValue in switchValues
	for switchCase in switchValues {
		switch switchValue {
			case switchCase: switchMatched := true
			default: switchMatched := false
		}
		AssertEq(switchMatched, switchValue == switchCase, A_LineNumber)
	}
switch "1" {
	case 1: switchResult := "numeric"
	default: switchResult := "string"
}
AssertEq(switchResult, "numeric", A_LineNumber)
switch 1.0 {
	case 2, 1: switchResult := "numeric"
	default: switchResult := "none"
}
AssertEq(switchResult, "numeric", A_LineNumber)

; A StringBuffer takes part as its text, as it does in ==.
switchBuffer := StringBuffer("abc")
switch switchBuffer {
	case "abc": switchResult := "text"
	default: switchResult := "identity"
}
AssertEq(switchResult, "text", A_LineNumber)
switch switchBuffer, "Off" {
	case "ABC": switchResult := "text"
	default: switchResult := "none"
}
AssertEq(switchResult, "text", A_LineNumber)

FileAppend "pass", "*"
