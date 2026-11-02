#NoTrayIcon
#ErrorStdOut
#Warn All, StdOut

#Import AHK
#Import Ks:DotNet { RegExReplace }
#Include <assert>
outputVarCount :=
match := RegExReplace("abc123123", "123$", "xyz")

AssertEq(match, "abc123xyz", A_LineNumber)

match := RegExReplace("abc123", "i)^ABC")

AssertEq(match, "123", A_LineNumber)

match := RegExReplace("abcXYZ123", "abc(.*)123", "aaa$1zzz")

AssertEq(match, "aaaXYZzzz", A_LineNumber)

match := RegExReplace("abc123abc456", "abc\d+", "", &outputVarCount)

AssertEq(match, "", A_LineNumber)
	
AssertEq(outputVarCount, 2, A_LineNumber)

; As in the global RegExReplace, a limit of 0 replaces nothing and a negative one replaces every match.
for limit, expected in Map(0, "a1a2a3|0", -5, "b1b2b3|3", 2, "b1b2a3|2")
	AssertEq(RegExReplace("a1a2a3", "a", "b", &outputVarCount, limit) "|" outputVarCount, expected, A_LineNumber)

; A StartingPos of 0 searches leftward from the start, so only a zero-width match can succeed there.
AssertEq(RegExReplace("abc", "c", "x",,, 0), "abc", A_LineNumber)
AssertEq(RegExReplace("abc", "", "x",,, 0), "xabc", A_LineNumber)

; A member call fills an output variable as the named import does.
AssertEq(DotNet.RegExReplace("a1a2", "a", "b", &memberCount), "b1b2", A_LineNumber)
AssertEq(memberCount, 2, A_LineNumber)

; The pattern is .NET's: character class subtraction is no PCRE syntax, and \K no .NET syntax.
AssertEq(RegExReplace("b", "[a-z-[aeiou]]", "x"), "x", A_LineNumber)
Throws(() => RegExReplace("ab", "a\Kb", "x"), A_LineNumber, Error)

; The named import leaves the global function where the AHK module holds it.
AssertEq(AHK.RegExReplace("b", "[a-z-[aeiou]]", "x"), "b", A_LineNumber)

FileAppend "pass", "*"
