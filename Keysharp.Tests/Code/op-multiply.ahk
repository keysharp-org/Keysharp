#NoTrayIcon
#Include <assert>

;
; Integers
;

x := 10 ; Positive int times positive int.
y := x * 100

Assert(y = 1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := 10 ; Positive int times negative int.
y := x * -100

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := -10 ; Negative int times positive int.
y := x * 100

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := -10 ; Negative int times negative int.
y := x * -100

Assert(y = 1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := 10 ; Negated positive int times positive int.
y := -x * 100

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := 10 ; Positive int times negated positive int.
y := 100 * -x

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := 0 ; Zero int times positive non-zero int.
y := x * 100

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := 100 ; Non-zero positive int times zero int.
y := x * 0

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := 0 ; Zero int times negative non-zero int.
y := x * -100

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := -100 ; Non-zero negative int times zero int.
y := x * 0

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := 0 ; Zero int times zero int.
y := x * 0

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

;
; Double
;

x := 10 ; Positive int times positive double.
y := x * 100.0

Assert(y = 1000.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 10 ; Positive int times negative double.
y := x * -100.0

Assert(y = -1000.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := -10.0 ; Negative double times positive int.
y := x * 100

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := -10.0 ; Negative double times negative int.
y := x * -100

Assert(y = 1000.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 10 ; Negated positive int times positive double.
y := -x * 100.0

Assert(y = -1000.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 10.0 ; Positive int times negated positive double.
y := 100 * -x

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 0 ; Zero int times positive non-zero double.
y := x * 100.0

Assert(y = 0.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 100.0 ; Non-zero positive double times zero int.
y := x * 0

Assert(y = 0.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 0 ; Zero int times negative non-zero double.
y := x * -100.0

Assert(y = 0.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := -100.0 ; Non-zero negative double times zero int.
y := x * 0

Assert(y = 0.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 0.0 ; Zero double times zero double.
y := x * 0.0

Assert(y = 0.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

;
; Integer strings
;

x := "10" ; Positive int string times positive int.
y := x * 100

Assert(y = 1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := 10 ; Positive int times negative int string.
y := x * "-100"

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "-10" ; Negative int string times positive int.
y := x * 100

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "-10" ; Negative int string times negative int string.
y := x * "-100"

Assert(y = 1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "10" ; Negated positive int string times positive int.
y := -x * 100

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "10" ; Positive int times negated positive int string.
y := 100 * -x

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0" ; Zero int string times positive non-zero int.
y := x * 100

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "100" ; Non-zero positive int string times zero int.
y := x * 0

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0" ; Zero int string times negative non-zero int.
y := x * -100

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "-100" ; Non-zero negative int string times zero int.
y := x * 0

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0" ; Zero int string times zero int string.
y := x * "0"

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

;
; Double strings
;

x := "10.0" ; Positive double string times positive int.
y := x * 100

Assert(y = 1000.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 10 ; Positive int times negative double string.
y := x * "-100.0"

Assert(y = -1000.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "-10.0" ; Negative double string times positive int.
y := x * 100

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "-10.0" ; Negative double string times negative double string.
y := x * "-100.0"

Assert(y = 1000.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "10.0" ; Negated positive double string times positive int.
y := -x * 100

Assert(y = -1000.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "10.0" ; Positive int times negated positive double string.
y := 100 * -x

Assert(y = -1000.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "0.0" ; Zero double string times positive non-zero int.
y := x * 100

Assert(y = 0.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "100.0" ; Non-zero positive double string times zero int.
y := x * 0

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "0.0" ; Zero double string times negative non-zero int.
y := x * -100

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "-100.0" ; Non-zero negative double string times zero int.
y := x * 0

Assert(y = 0.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "0.0" ; Zero double string times zero double string.
y := x * "0.0"

Assert(y = 0.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

;
; Hex strings times int and double
;

x := "0x0A" ; Positive hex string times positive int.
y := x * 100

Assert(y = 1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0x0A" ; Positive hex string times positive double.
y := x * 100.0

Assert(y = 1000.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := 10 ; Positive int times negative hex string.
y := x * "-0x64"

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := 10.0 ; Positive double times negative hex string.
y := x * "-0x64"

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "-0x0A" ; Negative hex string times positive int.
y := x * 100

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "-0x0A" ; Negative hex string times positive double.
y := x * 100.0

Assert(y = -1000.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "-0x0A" ; Negative hex string times negative int string.
y := x * "-100"

Assert(y = 1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)
	
x := "-0x0A" ; Negative hex string times negative double string.
y := x * "-100.0"

Assert(y = 1000.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "0x0A" ; Negated positive hex string times positive int.
y := -x * 100

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)
	
x := "0x0A" ; Negated positive hex string times positive double.
y := -x * 100.0

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "0x0A" ; Positive int times negated positive hex string.
y := 100 * -x

Assert(y = -1000.0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)
	
x := "0x0A" ; Positive double times negated positive hex string.
y := 100.0 * -x

Assert(y = -1000.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "0x00" ; Zero hex string times positive non-zero int.
y := x * 100

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)
	
x := "0x00" ; Zero hex string times positive non-zero double.
y := x * 100.0

Assert(y = 0.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "0x0A" ; Non-zero positive hex string times zero int.
y := x * 0

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0x0A" ; Non-zero positive hex string times zero double.
y := x * 0.0

Assert(y = 0.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "0x00" ; Zero hex string times negative non-zero int.
y := x * -100

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)
	
x := "0x00" ; Zero hex string times negative non-zero double.
y := x * -100.0

Assert(y = 0.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

x := "-0x64" ; Non-zero negative hex string times zero int.
y := x * 0

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "-0x64" ; Non-zero negative hex string times zero double.
y := x * 0.0

Assert(y = 0.0, A_LineNumber)
	
Assert(Type(y) = "Float", A_LineNumber)

;
; Hex strings
;

x := "0x0A" ; Positive hex string times positive hex string.
y := x * "0x64"

Assert(y = 1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0x0A" ; Positive hex string times negative hex string.
y := x * "-0x64"

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "-0x0A" ; Negative hex string times positive hex string.
y := x * "0x64"

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "-0x0A" ; Negative hex string times negative hex string.
y := x * "-0x64"

Assert(y = 1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)
	
x := "0x0A" ; Negated positive hex string times positive hex string.
y := -x * "0x64"

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0x0A" ; Positive hex string times negated positive hex string.
y := "0x64" * -x

Assert(y = -1000, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0x00" ; Zero hex string times positive non-zero hex string.
y := x * "0x64"

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0x0A" ; Non-zero positive hex string times zero hex string.
y := x * "0x00"

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0x00" ; Zero hex string times negative non-zero hex string.
y := x * "-0x64"

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "-0x64" ; Non-zero negative hex string times zero hex string.
y := x * "0x00"

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

x := "0x00" ; Zero hex string times zero hex string.
y := x * "0x00"

Assert(y = 0, A_LineNumber)
	
Assert(Type(y) = "Integer", A_LineNumber)

FileAppend "pass", "*"
